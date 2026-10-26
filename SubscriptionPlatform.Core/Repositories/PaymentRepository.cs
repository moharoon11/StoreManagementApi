using Dapper;
using SubscriptionPlatform.Api.Data;

namespace SubscriptionPlatform.Api.Repositories;

public sealed class PaymentRepository(ConnectionFactory connections)
{
    public async Task<bool> MarkFailedAsync(
        Guid appId,
        Guid paymentId,
        string orderId,
        string externalId)
    {
        await using var db = connections.Create();
        await db.OpenAsync();
        await using var transaction = await db.BeginTransactionAsync();
        var row = await db.QuerySingleOrDefaultAsync<dynamic>(
            "SELECT p.id,p.status,p.razorpay_order_id,p.subscription_id,s.status sub_status FROM payments p JOIN subscriptions s ON s.id=p.subscription_id JOIN customers c ON c.id=s.customer_id WHERE p.id=@paymentId AND s.application_id=@appId AND c.external_id=@externalId FOR UPDATE",
            new { paymentId, appId, externalId }, transaction);

        if (row is null || (string)row.status == "paid" || (string?)row.razorpay_order_id != orderId)
        {
            await transaction.RollbackAsync();
            return false;
        }

        if ((string)row.status != "failed")
        {
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

    public async Task<bool> VerifyAndActivateAsync(
        Guid appId,
        Guid paymentId,
        string orderId,
        string paymentProviderId,
        long providerAmount,
        string providerCurrency)
    {
        await using var db = connections.Create();
        await db.OpenAsync();
        await using var transaction = await db.BeginTransactionAsync();
        var payment = await db.QuerySingleOrDefaultAsync<dynamic>(
            "SELECT p.*,s.application_id,s.billing_period,s.status sub_status FROM payments p JOIN subscriptions s ON s.id=p.subscription_id WHERE p.id=@paymentId AND s.application_id=@appId FOR UPDATE",
            new { paymentId, appId }, transaction);

        if (payment is null)
        {
            return false;
        }

        if ((string)payment.status == "paid")
        {
            return (string?)payment.razorpay_order_id == orderId
                && (string?)payment.razorpay_payment_id == paymentProviderId;
        }

        if ((string?)payment.razorpay_order_id != orderId
            || (long)payment.amount_minor != providerAmount
            || !string.Equals((string)payment.currency, providerCurrency, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var now = DateTime.UtcNow;
        var end = (string)payment.billing_period switch
        {
            "three_month" => now.AddMonths(3),
            "six_month" => now.AddMonths(6),
            "one_year" or "yearly" => now.AddYears(1),
            _ => now.AddMonths(1)
        };

        await db.ExecuteAsync(
            "UPDATE payments SET status='paid',razorpay_payment_id=@providerId,paid_utc=@now WHERE id=@paymentId",
            new { providerId = paymentProviderId, now, paymentId }, transaction);
        await db.ExecuteAsync(
            "UPDATE subscriptions SET status='active',starts_utc=@now,ends_utc=@end,version=version+1 WHERE id=@sid",
            new { now, end, sid = (Guid)payment.subscription_id }, transaction);
        await db.ExecuteAsync(
            "INSERT INTO subscription_audits(subscription_id,from_status,to_status,reason,at_utc) VALUES(@sid,@from,'active','razorpay_captured_reconciled',@now)",
            new { sid = (Guid)payment.subscription_id, from = (string)payment.sub_status, now }, transaction);

        await transaction.CommitAsync();
        return true;
    }

    public async Task<IEnumerable<object>> HistoryAsync(Guid appId)
    {
        await using var db = connections.Create();
        return await db.QueryAsync(
            "SELECT p.id,p.subscription_id,p.amount_minor,p.currency,p.status,p.created_utc,p.paid_utc FROM payments p JOIN subscriptions s ON s.id=p.subscription_id WHERE s.application_id=@appId",
            new { appId });
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
        await using var db = connections.Create();
        await db.OpenAsync();
        await using var transaction = await db.BeginTransactionAsync();
        if (await db.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM webhook_events WHERE event_id=@eventId",
                new { eventId }, transaction) > 0)
        {
            await transaction.CommitAsync();
            return false;
        }

        await db.ExecuteAsync(
            "INSERT INTO webhook_events(event_id,event_type,received_utc) VALUES(@eventId,@eventType,UTC_TIMESTAMP(6))",
            new { eventId, eventType }, transaction);

        if (eventType is "payment.captured" or "payment.failed")
        {
            var payment = await db.QuerySingleOrDefaultAsync<dynamic>(
                "SELECT p.id,p.subscription_id,p.status,p.amount_minor,p.currency,p.razorpay_order_id,s.billing_period,s.status sub_status FROM payments p JOIN subscriptions s ON s.id=p.subscription_id WHERE p.razorpay_order_id=@orderId FOR UPDATE",
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
                    var now = DateTime.UtcNow;
                    var end = (string)payment.billing_period switch
                    {
                        "three_month" => now.AddMonths(3),
                        "six_month" => now.AddMonths(6),
                        "one_year" or "yearly" => now.AddYears(1),
                        _ => now.AddMonths(1)
                    };
                    await db.ExecuteAsync(
                        "UPDATE payments SET status='paid',razorpay_payment_id=@paymentProviderId,paid_utc=@now WHERE id=@id",
                        new { paymentProviderId, now, id = (Guid)payment.id }, transaction);
                    await db.ExecuteAsync(
                        "UPDATE subscriptions SET status='active',starts_utc=@now,ends_utc=@end,version=version+1 WHERE id=@sid",
                        new { now, end, sid = (Guid)payment.subscription_id }, transaction);
                    await db.ExecuteAsync(
                        "INSERT INTO subscription_audits(subscription_id,from_status,to_status,reason,at_utc) VALUES(@sid,@from,'active','captured_webhook_reconciled',@now)",
                        new { sid = (Guid)payment.subscription_id, from = (string)payment.sub_status, now }, transaction);
                }
                else if (eventType == "payment.failed" && (string)payment.status != "paid")
                {
                    await db.ExecuteAsync(
                        "UPDATE payments SET status='failed' WHERE id=@id",
                        new { id = (Guid)payment.id }, transaction);
                    await db.ExecuteAsync(
                        "UPDATE subscriptions SET status='expired',grace_ends_utc=NULL,version=version+1 WHERE id=@sid AND status IN ('pending_payment','grace')",
                        new { sid = (Guid)payment.subscription_id }, transaction);
                }
            }
        }

        await transaction.CommitAsync();
        return true;
    }
}
