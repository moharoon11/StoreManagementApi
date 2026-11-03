using Dapper;
using NLog;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SubscriptionPlatform.Api.Data;
using SubscriptionPlatform.Api.Models;

namespace SubscriptionPlatform.Api.Repositories;

public sealed class SubscriptionRepository(ConnectionFactory connections)
{
    private static readonly Logger Logger = LogManager.GetLogger("SubscriptionPlatform.Repositories.SubscriptionRepository");
    public sealed record OrderData(
        Guid SubscriptionId,
        Guid PaymentId,
        string Status,
        long Amount,
        string Currency,
        string? RazorpayOrderId = null);

    public async Task<OrderData?> CreateAsync(Guid appId, CreateSubscriptionRequest request)
    {
        try
        {
            // Keep customer checks, duplicate detection, and inserts in one transaction so two requests
            // cannot create two current subscriptions for the same customer.
            await using var db = await connections.OpenConnectionAsync();
            await using var transaction = await db.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);

            // The customer must belong to this application; the row lock also serializes competing starts.
            var customer = await db.ExecuteScalarAsync<Guid?>(
                "SELECT id FROM customers WHERE id=@CustomerId AND application_id=@appId FOR UPDATE",
                new { request.CustomerId, appId }, transaction);
            // The active plan is the source of truth for prices, currency, and trial rules.
            var plan = await db.QuerySingleOrDefaultAsync<dynamic>(
                "SELECT currency,pricing_json,trial_days FROM plans WHERE id=@PlanId AND application_id=@appId AND active=1",
                new { request.PlanId, appId }, transaction);

            if (customer is null || plan is null)
            {
                // Returning null prevents a caller from using another tenant's customer or plan.
                Logger.Warn(this.MethodName() + " | Subscription creation rejected: customer or plan unavailable. ApplicationId: {0}", appId);
                return null;
            }

            // Hash the retry key with the application ID so different apps can safely reuse the same key.
            var scopedKey = Convert.ToHexStringLower(
                SHA256.HashData(Encoding.UTF8.GetBytes($"{appId:N}:{request.IdempotencyKey}")));
            // Look up the first attempt before inserting anything new.
            var prior = await db.QuerySingleOrDefaultAsync<dynamic>(
                "SELECT p.id,p.subscription_id,p.status,p.amount_minor,p.currency,p.razorpay_order_id FROM payments p JOIN subscriptions s ON s.id=p.subscription_id WHERE p.idempotency_key=@key AND s.application_id=@appId",
                new { key = scopedKey, appId }, transaction);

            if (prior is not null)
            {
                // Return the original IDs and state so the create operation is safe to retry.
                Logger.Info(this.MethodName() + " | Existing checkout reused. SubscriptionId: {0}", (Guid)prior.subscription_id);
                return new(
                    (Guid)prior.subscription_id,
                    (Guid)prior.id,
                    (string)prior.status,
                    (long)prior.amount_minor,
                    (string)prior.currency,
                    (string?)prior.razorpay_order_id);
            }

            // Only expired or cancelled subscriptions allow another subscription to start.
            if (await db.ExecuteScalarAsync<int>(
                    "SELECT COUNT(*) FROM subscriptions WHERE customer_id=@customerId AND status NOT IN ('expired','cancelled')",
                    new { customerId = request.CustomerId }, transaction) > 0)
            {
                Logger.Warn(this.MethodName() + " | Subscription creation rejected: customer already has a current subscription. CustomerId: {0}", request.CustomerId);
                throw new InvalidOperationException("Customer already has a current subscription.");
            }

            // Trial eligibility depends on the customer's complete subscription history.
            var priorSubscriptions = await db.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM subscriptions WHERE customer_id=@customerId",
                new { customerId = request.CustomerId }, transaction);
            // Read the configured trial length from the selected plan.
            var trialDays = (int)plan.trial_days;

            if (request.StartTrial && (trialDays == 0 || priorSubscriptions > 0))
            {
                // Reject trials when the plan has no trial period or the customer has used subscription history.
                Logger.Warn(this.MethodName() + " | Trial rejected: no trial period or prior subscription exists. CustomerId: {0}", request.CustomerId);
                throw new InvalidOperationException("Trial is not eligible.");
            }

            string pricingJson = plan.pricing_json is null ? "[]" : Convert.ToString(plan.pricing_json) ?? "[]";
            var options = JsonSerializer.Deserialize<List<PlanOptionRequest>>(
                pricingJson,
                new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? new List<PlanOptionRequest>();
            var selectedOption = request.PlanOptionId is int optionId
                ? options.SingleOrDefault(option => option.Id == optionId && option.Active)
                : options.SingleOrDefault(option => string.Equals(option.Code, request.BillingPeriod, StringComparison.OrdinalIgnoreCase) && option.Active);

            if (!request.StartTrial && selectedOption is null)
            {
                Logger.Warn(this.MethodName() + " | Selected plan option was not found. PlanId: {0}, OptionId: {1}, Code: {2}", request.PlanId, request.PlanOptionId, request.BillingPeriod);
                throw new InvalidOperationException("The selected subscription option is unavailable.");
            }

            // These server-created IDs are the stable references used by later payment requests.
            var subscriptionId = Guid.NewGuid();
            var paymentId = Guid.NewGuid();
            // Read the amount, currency, and duration from the selected JSON option.
            var amount = request.StartTrial ? 0 : selectedOption!.AmountMinor;
            var durationSeconds = request.StartTrial
                ? trialDays * 86400
                : selectedOption!.DurationSeconds > 0
                    ? selectedOption.DurationSeconds
                    : selectedOption.DurationDays * 86400;

            if (!request.StartTrial && amount <= 0)
            {
                // Do not create a paid checkout when the selected term has no positive price.
                Logger.Error(this.MethodName() + " | Paid term has no configured price. PlanId: {0}, BillingPeriod: {1}", request.PlanId, request.BillingPeriod);
                throw new InvalidOperationException("This paid term has no configured price.");
            }

            // A trial gets an end date now; a paid term waits for payment before its real term begins.
            var currency = request.StartTrial ? (string)plan.currency : selectedOption!.Currency;
            var now = DateTime.UtcNow;
            var status = request.StartTrial ? "trialing" : "pending_payment";
            var billingPeriod = request.StartTrial ? "trial" : selectedOption!.Code;
            var durationDays = Math.Max(1, (int)Math.Ceiling(durationSeconds / 86400d));
            var ends = request.StartTrial ? now.AddDays(trialDays) : now;

            // Save the local subscription before contacting Razorpay so the order has a local owner.
            await db.ExecuteAsync(
                "INSERT INTO subscriptions(id,application_id,customer_id,plan_id,status,billing_period,duration_days,duration_seconds,starts_utc,ends_utc,version,created_utc) VALUES(@subscriptionId,@appId,@CustomerId,@PlanId,@status,@BillingPeriod,@DurationDays,@DurationSeconds,@now,@ends,0,@now)",
                new { subscriptionId, appId, request.CustomerId, request.PlanId, status, BillingPeriod = billingPeriod, DurationDays = durationDays, DurationSeconds = durationSeconds, now, ends }, transaction);

            if (request.StartTrial)
            {
                // Keep an audit trail showing that the subscription started through an eligible trial.
                await db.ExecuteAsync(
                    "INSERT INTO subscription_audits(subscription_id,from_status,to_status,reason,at_utc) VALUES(@subscriptionId,'new','trialing','eligible_trial_started',@now)",
                    new { subscriptionId, now }, transaction);
                // The trial is ready immediately, so it needs no payment row or provider order.
                await transaction.CommitAsync();
                Logger.Info(this.MethodName() + " | Trial committed. SubscriptionId: {0}", subscriptionId);
                return new(subscriptionId, Guid.Empty, status, 0, currency);
            }

            // This payment ID is the local checkout reference; Razorpay's order ID is added afterward.
            await db.ExecuteAsync(
                "INSERT INTO payments(id,subscription_id,idempotency_key,amount_minor,currency,status,created_utc) VALUES(@paymentId,@subscriptionId,@key,@amount,@currency,'order_pending',@now)",
                new { paymentId, subscriptionId, key = scopedKey, amount, currency, now }, transaction);
            // Commit before contacting Razorpay so an interrupted request can find and reuse this payment.
            await transaction.CommitAsync();
            Logger.Info(this.MethodName() + " | Paid subscription checkout committed. SubscriptionId: {0}, PaymentId: {1}", subscriptionId, paymentId);

            // The payment row is ready for Razorpay order creation. The payment status
            // is intentionally returned here, not the subscription status.
            return new(subscriptionId, paymentId, "order_pending", amount, currency);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, this.MethodName() + " | Repository operation failed.");
            throw;
        }
    }
    public async Task SetOrderAsync(Guid paymentId, string orderId)
    {
        try
        {
            await using var db = await connections.OpenConnectionAsync();
            var affected = await db.ExecuteAsync(
                "UPDATE payments SET razorpay_order_id=@orderId WHERE id=@paymentId",
                new { paymentId, orderId });
            if (affected > 0)
                Logger.Info(this.MethodName() + " | Provider order linked. PaymentId: {0}, OrderId: {1}", paymentId, orderId);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, this.MethodName() + " | Repository operation failed.");
            throw;
        }
    }

    public async Task MarkOrderFailedAsync(Guid paymentId)
    {
        try
        {
            await using var db = await connections.OpenConnectionAsync();
            var affected = await db.ExecuteAsync(
                "UPDATE payments SET status='order_failed' WHERE id=@paymentId",
                new { paymentId });
            if (affected > 0)
                Logger.Warn(this.MethodName() + " | Provider order creation marked failed. PaymentId: {0}", paymentId);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, this.MethodName() + " | Repository operation failed.");
            throw;
        }
    }

    public async Task<object?> GetAsync(Guid id, Guid appId)
    {
        try
        {
            await using var db = await connections.OpenConnectionAsync();
            var subscription = await db.QuerySingleOrDefaultAsync(
                "SELECT s.*,p.name plan_name FROM subscriptions s JOIN plans p ON p.id=s.plan_id WHERE s.id=@id AND s.application_id=@appId",
                new { id, appId });
            Logger.Debug(this.MethodName() + " | Subscription found: {0}. SubscriptionId: {1}, ApplicationId: {2}", subscription is not null, id, appId);
            return subscription;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, this.MethodName() + " | Repository operation failed.");
            throw;
        }
    }

    public async Task<object?> GetByExternalCustomerAsync(Guid appId, string externalId)
    {
        try
        {
            await using var db = await connections.OpenConnectionAsync();
            var subscription = await db.QuerySingleOrDefaultAsync(
                "SELECT s.id subscription_id,s.status,s.billing_period,s.starts_utc,s.ends_utc,s.grace_ends_utc,s.cancel_at_period_end,CASE WHEN s.status IN ('active','trialing') AND s.ends_utc>UTC_TIMESTAMP(6) THEN 1 WHEN s.status='grace' AND s.grace_ends_utc>UTC_TIMESTAMP(6) THEN 1 ELSE 0 END has_access FROM subscriptions s JOIN customers c ON c.id=s.customer_id WHERE s.application_id=@appId AND c.external_id=@externalId ORDER BY s.created_utc DESC LIMIT 1",
                new { appId, externalId });
            Logger.Debug(this.MethodName() + " | Customer subscription found: {0}. ApplicationId: {1}, ExternalId: {2}", subscription is not null, appId, externalId);
            return subscription;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, this.MethodName() + " | Repository operation failed.");
            throw;
        }
    }

    public async Task<bool> CancelAsync(Guid id, Guid appId)
    {
        try
        {
            await using var db = await connections.OpenConnectionAsync();
            var changed = await db.ExecuteAsync(
                "UPDATE subscriptions SET cancel_at_period_end=1,version=version+1 WHERE id=@id AND application_id=@appId AND status IN ('active','trialing')",
                new { id, appId }) > 0;
            if (changed)
                Logger.Info(this.MethodName() + " | Subscription cancellation scheduled. SubscriptionId: {0}", id);
            return changed;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, this.MethodName() + " | Repository operation failed.");
            throw;
        }
    }

    public async Task<bool> ChangePlanAsync(Guid id, Guid appId, Guid planId)
    {
        try
        {
            await using var db = await connections.OpenConnectionAsync();
            var changed = await db.ExecuteAsync(
                "UPDATE subscriptions s JOIN plans p ON p.id=@planId AND p.application_id=@appId AND p.active=1 SET s.plan_id=p.id,s.version=s.version+1 WHERE s.id=@id AND s.application_id=@appId AND s.status IN ('active','trialing')",
                new { id, planId, appId }) > 0;
            if (changed)
                Logger.Info(this.MethodName() + " | Subscription plan changed. SubscriptionId: {0}, PlanId: {1}", id, planId);
            return changed;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, this.MethodName() + " | Repository operation failed.");
            throw;
        }
    }
}
