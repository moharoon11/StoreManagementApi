using Dapper;
using NLog;
using SubscriptionPlatform.Api.Data;

namespace SubscriptionPlatform.Api.Repositories;

public sealed class PaymentRepository(ConnectionFactory connections)
{
    private static readonly Logger Logger = LogManager.GetLogger("SubscriptionPlatform.Repositories.PaymentRepository");
    public async Task<bool> MarkFailedAsync(
        Guid appId,
        Guid paymentId,
        string orderId,
        string externalId)
    {
        try
        {
            // Keep the payment and subscription changes together so a failed checkout cannot leave access pending.
            await using var db = await connections.OpenConnectionAsync();
            await using var transaction = await db.BeginTransactionAsync();
            // Find the exact local payment and prove that both the customer and application own it.
            var row = await db.QuerySingleOrDefaultAsync<dynamic>(
                "SELECT p.id,p.status,p.razorpay_order_id,p.subscription_id,s.status sub_status FROM payments p JOIN subscriptions s ON s.id=p.subscription_id JOIN customers c ON c.id=s.customer_id WHERE p.id=@paymentId AND s.application_id=@appId AND c.external_id=@externalId FOR UPDATE",
                new { paymentId, appId, externalId }, transaction);

            if (row is null || (string)row.status == "paid" || (string?)row.razorpay_order_id != orderId)
            {
                // Never let an unknown or already-paid order be changed by a failure callback.
                Logger.Warn(this.MethodName() + " | Failure callback rejected: payment missing, already paid, or order mismatch. PaymentId: {0}", paymentId);
                await transaction.RollbackAsync();
                return false;
            }

            if ((string)row.status != "failed")
            {
                // Save the failed provider attempt so the customer can start a new checkout.
                await db.ExecuteAsync(
                    "UPDATE payments SET status='failed' WHERE id=@paymentId AND status<>'paid'",
                    new { paymentId }, transaction);
                await db.ExecuteAsync(
                    "UPDATE subscriptions SET status='expired',version=version+1 WHERE id=@sid AND status IN ('pending_payment','grace')",
                    new { sid = (Guid)row.subscription_id }, transaction);
                await db.ExecuteAsync(
                    "INSERT INTO subscription_audits(subscription_id,from_status,to_status,reason,at_utc) VALUES(@sid,@from,'expired','checkout_payment_failed',UTC_TIMESTAMP(6))",
                    new { sid = (Guid)row.subscription_id, from = (string)row.sub_status }, transaction);
            }

            await transaction.CommitAsync();
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, this.MethodName() + " | Repository operation failed.");
            throw;
        }
    }

    public async Task<bool> VerifyAndActivateAsync(
        Guid appId,
        Guid paymentId,
        string orderId,
        string paymentProviderId,
        long providerAmount,
        string providerCurrency)
    {
        try
        {
            // Activate access only if every payment and subscription update succeeds together.
            await using var db = await connections.OpenConnectionAsync();
            await using var transaction = await db.BeginTransactionAsync();
            // Lock the local payment while checking the provider response against stored values.
            var payment = await db.QuerySingleOrDefaultAsync<dynamic>(
            "SELECT p.*,s.application_id,s.billing_period,s.duration_seconds,s.status sub_status FROM payments p JOIN subscriptions s ON s.id=p.subscription_id WHERE p.id=@paymentId AND s.application_id=@appId FOR UPDATE",
                new { paymentId, appId }, transaction);

            if (payment is null)
            {
                // Return false when the payment cannot be found or is owned by another application.
                Logger.Warn(this.MethodName() + " | Payment activation rejected: local payment not found. PaymentId: {0}", paymentId);
                return false;
            }

            if ((string)payment.status == "paid")
            {
                // A repeated verify request is safe when it refers to the same provider payment.
                var samePayment = (string?)payment.razorpay_order_id == orderId
                    && (string?)payment.razorpay_payment_id == paymentProviderId;
                if (!samePayment)
                    Logger.Warn(this.MethodName() + " | Paid payment verification rejected: provider IDs differ. PaymentId: {0}", paymentId);
                return samePayment;
            }

            if ((string?)payment.razorpay_order_id != orderId
                || (long)payment.amount_minor != providerAmount
                || !string.Equals((string)payment.currency, providerCurrency, StringComparison.OrdinalIgnoreCase))
            {
                // The client cannot activate access with a different order, price, or currency.
                Logger.Warn(this.MethodName() + " | Payment activation rejected: order, amount, or currency mismatch. PaymentId: {0}", paymentId);
                return false;
            }

            // The paid term starts at verification time, not when the checkout order was created.
            var now = DateTime.UtcNow;
        var end = now.AddSeconds((int)payment.duration_seconds);

            // Save the provider payment ID so later retries recognize the same successful payment.
            await db.ExecuteAsync(
                "UPDATE payments SET status='paid',razorpay_payment_id=@providerId,paid_utc=@now WHERE id=@paymentId",
                new { providerId = paymentProviderId, now, paymentId }, transaction);
            // Grant access only after the local payment has been confirmed as paid.
            await db.ExecuteAsync(
                "UPDATE subscriptions SET status='active',starts_utc=@now,ends_utc=@end,version=version+1 WHERE id=@sid",
                new { now, end, sid = (Guid)payment.subscription_id }, transaction);
            // Keep a readable history of the change from pending payment to active.
            await db.ExecuteAsync(
                "INSERT INTO subscription_audits(subscription_id,from_status,to_status,reason,at_utc) VALUES(@sid,@from,'active','razorpay_captured_reconciled',@now)",
                new { sid = (Guid)payment.subscription_id, from = (string)payment.sub_status, now }, transaction);

            // Make payment and access activation visible as one completed operation.
            await transaction.CommitAsync();
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, this.MethodName() + " | Repository operation failed.");
            throw;
        }
    }

    public async Task<IEnumerable<object>> HistoryAsync(Guid appId)
    {
        try
        {
            // Return payment records only for subscriptions belonging to this application.
            await using var db = await connections.OpenConnectionAsync();
            var payments = (await db.QueryAsync(
                "SELECT p.id,p.subscription_id,p.amount_minor,p.currency,p.status,p.created_utc,p.paid_utc FROM payments p JOIN subscriptions s ON s.id=p.subscription_id WHERE s.application_id=@appId",
                new { appId })).AsList();
            Logger.Debug(this.MethodName() + " | Payment history loaded. ApplicationId: {0}, Count: {1}", appId, payments.Count);
            return payments;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, this.MethodName() + " | Repository operation failed.");
            throw;
        }
    }

    public async Task<bool> ProcessWebhookAsync(
        string eventId,
        string eventType,
        string? orderId,
        string? paymentProviderId,
        long? providerAmount,
        string? providerCurrency,
        string? providerStatus)
    {
        try
        {
            // Open a transaction so duplicate detection and event processing cannot interleave.
            await using var db = await connections.OpenConnectionAsync();
            await using var transaction = await db.BeginTransactionAsync();
            if (await db.ExecuteScalarAsync<int>(
                    "SELECT COUNT(*) FROM webhook_events WHERE event_id=@eventId",
                    new { eventId }, transaction) > 0)
            {
                // Razorpay may retry events, so an event ID already stored must have no second effect.
                Logger.Debug(this.MethodName() + " | Duplicate webhook event ignored. EventId: {0}", eventId);
                await transaction.CommitAsync();
                return false;
            }

            // Save the event ID in the same transaction as its effect to make webhook processing repeat-safe.
            await db.ExecuteAsync(
                "INSERT INTO webhook_events(event_id,event_type,received_utc) VALUES(@eventId,@eventType,UTC_TIMESTAMP(6))",
                new { eventId, eventType }, transaction);

            if (eventType is "payment.captured" or "payment.failed")
            {
                // Find and lock the local payment associated with the provider order.
                var payment = await db.QuerySingleOrDefaultAsync<dynamic>(
                "SELECT p.id,p.subscription_id,p.status,p.amount_minor,p.currency,p.razorpay_order_id,s.billing_period,s.duration_seconds,s.status sub_status FROM payments p JOIN subscriptions s ON s.id=p.subscription_id WHERE p.razorpay_order_id=@orderId FOR UPDATE",
                    new { orderId }, transaction);
                if (payment is not null)
                {
                    if (eventType == "payment.captured"
                        && (string)payment.status != "paid"
                        && providerStatus == "captured"
                        && (long)payment.amount_minor == providerAmount
                        && string.Equals((string)payment.currency, providerCurrency, StringComparison.OrdinalIgnoreCase)
                        && (string?)payment.razorpay_order_id == orderId)
                    {
                        // A captured event can activate access only when it matches the stored order and amount.
                        // The paid term starts when the provider confirms capture.
                        var now = DateTime.UtcNow;
                    var end = now.AddSeconds((int)payment.duration_seconds);
                        // Save the provider payment ID for future duplicate-event checks.
                        await db.ExecuteAsync(
                            "UPDATE payments SET status='paid',razorpay_payment_id=@paymentProviderId,paid_utc=@now WHERE id=@id",
                            new { paymentProviderId, now, id = (Guid)payment.id }, transaction);
                        // Grant access only after the payment is marked paid locally.
                        await db.ExecuteAsync(
                            "UPDATE subscriptions SET status='active',starts_utc=@now,ends_utc=@end,version=version+1 WHERE id=@sid",
                            new { now, end, sid = (Guid)payment.subscription_id }, transaction);
                        // Record that the webhook, rather than the checkout callback, caused activation.
                        await db.ExecuteAsync(
                            "INSERT INTO subscription_audits(subscription_id,from_status,to_status,reason,at_utc) VALUES(@sid,@from,'active','captured_webhook_reconciled',@now)",
                            new { sid = (Guid)payment.subscription_id, from = (string)payment.sub_status, now }, transaction);
                    }
                    else if (eventType == "payment.failed" && (string)payment.status != "paid")
                    {
                        // A provider failure closes the pending checkout and prevents access from remaining open.
                        await db.ExecuteAsync(
                            "UPDATE payments SET status='failed' WHERE id=@id",
                            new { id = (Guid)payment.id }, transaction);
                        await db.ExecuteAsync(
                            "UPDATE subscriptions SET status='expired',grace_ends_utc=NULL,version=version+1 WHERE id=@sid AND status IN ('pending_payment','grace')",
                            new { sid = (Guid)payment.subscription_id }, transaction);
                    }
                    else if ((string)payment.status == "paid")
                    {
                        Logger.Debug(this.MethodName() + " | Webhook ignored: local payment is already paid. EventId: {0}, EventType: {1}", eventId, eventType);
                    }
                    else
                    {
                        Logger.Warn(this.MethodName() + " | Webhook payment did not meet reconciliation conditions. EventId: {0}, EventType: {1}, PaymentStatus: {2}", eventId, eventType, (string)payment.status);
                    }
                }
                else
                {
                    Logger.Warn(this.MethodName() + " | Webhook order has no matching local payment. EventId: {0}, OrderId: {1}", eventId, orderId);
                }
            }

            await transaction.CommitAsync();
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, this.MethodName() + " | Repository operation failed.");
            throw;
        }
    }
}
            // Expire the related subscription while it is still waiting for payment.
            // Keep an audit record explaining why access was ended.
