using SubscriptionPlatform.Api.Models;
using SubscriptionPlatform.Api.Repositories;
using NLog;

namespace SubscriptionPlatform.Api.Services;

public sealed class SubscriptionService(
    SubscriptionRepository repository,
    RazorpayService razorpay)
{
    private static readonly Logger Logger = LogManager.GetLogger("SubscriptionPlatform.Services.SubscriptionService");
    public async Task<SubscriptionCreatedResponse?> CreateAsync(
        Guid appId,
        CreateSubscriptionRequest request)
    {
        // This is the business layer for creating a subscription. It validates the request,
        // creates the local records, and creates a Razorpay order only for a paid subscription.
        Logger.Debug($"{this.MethodName()} | ApplicationId: {appId} | CustomerId: {request.CustomerId} | PlanId: {request.PlanId} | " +
            $"Payload: BillingPeriod={request.BillingPeriod}, StartTrial={request.StartTrial}");
        if (request.BillingPeriod is not ("three_month" or "six_month" or "one_year" or "monthly" or "yearly")
            || string.IsNullOrWhiteSpace(request.IdempotencyKey)
            || request.IdempotencyKey.Length > 80)
        {
            // Reject unsupported billing periods and unusable idempotency keys before accessing the database.
            Logger.Warn(this.MethodName() + " | Subscription request validation failed: billing period or idempotency key invalid. ApplicationId: {0}", appId);
            throw new ArgumentException("Billing period or idempotency key is invalid.");
        }

        try
        {
            // The repository creates the subscription first. For paid terms it also creates
            // a pending payment row that will be linked to Razorpay below.
            var result = await repository.CreateAsync(appId, request);

            if (result is null)
            {
                // Return null when the customer or plan was not found for this application.
                Logger.Warn($"{this.MethodName()} | ApplicationId: {appId} | CustomerId: {request.CustomerId} | Payload: no result returned");
                return null;
            }

            if (request.StartTrial || result.Status == "trialing")
            {
                // A trial is already usable, so it does not need a payment row or Razorpay order.
                Logger.Info($"{this.MethodName()} | ApplicationId: {appId} | SubscriptionId: {result.SubscriptionId} | Status: {result.Status}");
                return new(result.SubscriptionId, result.Status);
            }

            // A retry that already has an order must reuse that order. Creating a second order
            // could make the customer pay twice for the same subscription attempt.
            if (!string.IsNullOrWhiteSpace(result.RazorpayOrderId))
            {
                // Reuse the existing provider order when this is an idempotent retry.
                return new(
                    result.SubscriptionId,
                    "pending_payment",
                    result.PaymentId,
                    result.RazorpayOrderId,
                    razorpay.KeyId,
                    result.Amount,
                    result.Currency);
            }

            if (result.Status != "order_pending")
            {
                // Return the stored state when the payment was already failed, paid, or expired.
                return new(result.SubscriptionId, result.Status, result.PaymentId);
            }

            try
            {
                // The amount comes from the plan in the database, so the client cannot change the price.
                var orderId = razorpay.CreateOrder(
                    result.Amount,
                    result.Currency,
                    result.PaymentId.ToString("N"));
                // Store Razorpay's order ID so later verify/failure/webhook requests can be matched
                // to this exact local payment row.
                await repository.SetOrderAsync(result.PaymentId, orderId);

                return new(
                    result.SubscriptionId,
                    "pending_payment",
                    result.PaymentId,
                    orderId,
                    razorpay.KeyId,
                    result.Amount,
                    result.Currency);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"{this.MethodName()} | Razorpay order creation failed | PaymentId: {result.PaymentId}");
                await repository.MarkOrderFailedAsync(result.PaymentId);
                throw;
            }
        }
        catch (Exception ex)
        {
            Logger.Error(ex, $"{this.MethodName()} | ApplicationId: {appId} | CustomerId: {request.CustomerId} | Exception: {ex.Message}");
            throw;
        }
    }
    public Task<object?> GetAsync(Guid id, Guid appId)
    {
        // Retrieve one subscription while restricting the query to the application owner.
        return repository.GetAsync(id, appId);
    }

    public Task<object?> GetByExternalCustomerAsync(Guid appId, string externalId)
    {
        // Retrieve the latest subscription for a customer's external application ID.
        return repository.GetByExternalCustomerAsync(appId, externalId);
    }

    public Task<bool> CancelAsync(Guid id, Guid appId)
    {
        // Set the period-end cancellation flag; this does not remove access immediately.
        return repository.CancelAsync(id, appId);
    }

    public Task<bool> ChangePlanAsync(Guid id, Guid appId, Guid planId)
    {
        // Replace the plan only for an active/trialing subscription; the repository enforces ownership.
        return repository.ChangePlanAsync(id, appId, planId);
    }
}
