using Dapper;
using System.Security.Cryptography;
using System.Text;
using SubscriptionPlatform.Api.Data;
using SubscriptionPlatform.Api.Models;

namespace SubscriptionPlatform.Api.Repositories;

public sealed class SubscriptionRepository(ConnectionFactory connections)
{
    public sealed record OrderData(
        Guid SubscriptionId,
        Guid PaymentId,
        string Status,
        long Amount,
        string Currency,
        string? RazorpayOrderId = null);

    public async Task<OrderData?> CreateAsync(Guid appId, CreateSubscriptionRequest request)
    {
        await using var db = connections.Create();
        await db.OpenAsync();
        await using var transaction = await db.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);

        var customer = await db.ExecuteScalarAsync<Guid?>(
            "SELECT id FROM customers WHERE id=@CustomerId AND application_id=@appId FOR UPDATE",
            new { request.CustomerId, appId }, transaction);
        var plan = await db.QuerySingleOrDefaultAsync<dynamic>(
            "SELECT currency,monthly_amount_minor,yearly_amount_minor,three_month_amount_minor,six_month_amount_minor,trial_days FROM plans WHERE id=@PlanId AND application_id=@appId AND active=1",
            new { request.PlanId, appId }, transaction);

        if (customer is null || plan is null)
        {
            return null;
        }

        var scopedKey = Convert.ToHexStringLower(
            SHA256.HashData(Encoding.UTF8.GetBytes($"{appId:N}:{request.IdempotencyKey}")));
        var prior = await db.QuerySingleOrDefaultAsync<dynamic>(
            "SELECT p.id,p.subscription_id,p.status,p.amount_minor,p.currency,p.razorpay_order_id FROM payments p JOIN subscriptions s ON s.id=p.subscription_id WHERE p.idempotency_key=@key AND s.application_id=@appId",
            new { key = scopedKey, appId }, transaction);

        if (prior is not null)
        {
            return new(
                (Guid)prior.subscription_id,
                (Guid)prior.id,
                (string)prior.status,
                (long)prior.amount_minor,
                (string)prior.currency,
                (string?)prior.razorpay_order_id);
        }

        if (await db.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM subscriptions WHERE customer_id=@customerId AND status NOT IN ('expired','cancelled')",
                new { customerId = request.CustomerId }, transaction) > 0)
        {
            throw new InvalidOperationException("Customer already has a current subscription.");
        }

        var priorSubscriptions = await db.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM subscriptions WHERE customer_id=@customerId",
            new { customerId = request.CustomerId }, transaction);
        var trialDays = (int)plan.trial_days;

        if (request.StartTrial && (trialDays == 0 || priorSubscriptions > 0))
        {
            throw new InvalidOperationException("Trial is not eligible.");
        }

        var subscriptionId = Guid.NewGuid();
        var paymentId = Guid.NewGuid();
        var amount = request.BillingPeriod switch
        {
            "three_month" => (long)plan.three_month_amount_minor,
            "six_month" => (long)plan.six_month_amount_minor,
            "one_year" => (long)plan.yearly_amount_minor,
            "yearly" => (long)plan.yearly_amount_minor,
            _ => (long)plan.monthly_amount_minor
        };

        if (!request.StartTrial && amount <= 0)
        {
            throw new InvalidOperationException("This paid term has no configured price.");
        }

        var currency = (string)plan.currency;
        var now = DateTime.UtcNow;
        var status = request.StartTrial ? "trialing" : "pending_payment";
        var ends = request.StartTrial ? now.AddDays(trialDays) : now;

        await db.ExecuteAsync(
            "INSERT INTO subscriptions(id,application_id,customer_id,plan_id,status,billing_period,starts_utc,ends_utc,version,created_utc) VALUES(@subscriptionId,@appId,@CustomerId,@PlanId,@status,@BillingPeriod,@now,@ends,0,@now)",
            new { subscriptionId, appId, request.CustomerId, request.PlanId, status, request.BillingPeriod, now, ends }, transaction);

        if (request.StartTrial)
        {
            await db.ExecuteAsync(
                "INSERT INTO subscription_audits(subscription_id,from_status,to_status,reason,at_utc) VALUES(@subscriptionId,'new','trialing','eligible_trial_started',@now)",
                new { subscriptionId, now }, transaction);
            await transaction.CommitAsync();
            return new(subscriptionId, Guid.Empty, status, 0, currency);
        }

        await db.ExecuteAsync(
            "INSERT INTO payments(id,subscription_id,idempotency_key,amount_minor,currency,status,created_utc) VALUES(@paymentId,@subscriptionId,@key,@amount,@currency,'order_pending',@now)",
            new { paymentId, subscriptionId, key = scopedKey, amount, currency, now }, transaction);
        await transaction.CommitAsync();

        // The payment row is ready for Razorpay order creation. The payment status
        // is intentionally returned here, not the subscription status.
        return new(subscriptionId, paymentId, "order_pending", amount, currency);
    }
    public async Task SetOrderAsync(Guid paymentId, string orderId)
    {
        await using var db = connections.Create();
        await db.ExecuteAsync(
            "UPDATE payments SET razorpay_order_id=@orderId WHERE id=@paymentId",
            new { paymentId, orderId });
    }

    public async Task MarkOrderFailedAsync(Guid paymentId)
    {
        await using var db = connections.Create();
        await db.ExecuteAsync(
            "UPDATE payments SET status='order_failed' WHERE id=@paymentId",
            new { paymentId });
    }

    public async Task<object?> GetAsync(Guid id, Guid appId)
    {
        await using var db = connections.Create();
        return await db.QuerySingleOrDefaultAsync(
            "SELECT s.*,p.name plan_name FROM subscriptions s JOIN plans p ON p.id=s.plan_id WHERE s.id=@id AND s.application_id=@appId",
            new { id, appId });
    }

    public async Task<object?> GetByExternalCustomerAsync(Guid appId, string externalId)
    {
        await using var db = connections.Create();
        return await db.QuerySingleOrDefaultAsync(
            "SELECT s.id subscription_id,s.status,s.billing_period,s.starts_utc,s.ends_utc,s.grace_ends_utc,s.cancel_at_period_end,CASE WHEN s.status IN ('active','trialing') AND s.ends_utc>UTC_TIMESTAMP(6) THEN 1 WHEN s.status='grace' AND s.grace_ends_utc>UTC_TIMESTAMP(6) THEN 1 ELSE 0 END has_access FROM subscriptions s JOIN customers c ON c.id=s.customer_id WHERE s.application_id=@appId AND c.external_id=@externalId ORDER BY s.created_utc DESC LIMIT 1",
            new { appId, externalId });
    }

    public async Task<bool> CancelAsync(Guid id, Guid appId)
    {
        await using var db = connections.Create();
        return await db.ExecuteAsync(
            "UPDATE subscriptions SET cancel_at_period_end=1,version=version+1 WHERE id=@id AND application_id=@appId AND status IN ('active','trialing')",
            new { id, appId }) > 0;
    }

    public async Task<bool> ChangePlanAsync(Guid id, Guid appId, Guid planId)
    {
        await using var db = connections.Create();
        return await db.ExecuteAsync(
            "UPDATE subscriptions s JOIN plans p ON p.id=@planId AND p.application_id=@appId AND p.active=1 SET s.plan_id=p.id,s.version=s.version+1 WHERE s.id=@id AND s.application_id=@appId AND s.status IN ('active','trialing')",
            new { id, planId, appId }) > 0;
    }
}
