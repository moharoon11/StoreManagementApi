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
                var trial = new SubscriptionCreatedResponse(result.SubscriptionId, result.Status);
                Logger.Info(this.MethodName() + " | Trial returned. ApplicationId: {0}, SubscriptionId: {1}, Status: {2}", appId, trial.SubscriptionId, trial.Status);
                return trial;
            }

            // A retry that already has an order must reuse that order. Creating a second order
            // could make the customer pay twice for the same subscription attempt.
            if (!string.IsNullOrWhiteSpace(result.RazorpayOrderId))
            {
                // Reuse the existing provider order when this is an idempotent retry.
                var existingOrder = new SubscriptionCreatedResponse(
                    result.SubscriptionId,
                    "pending_payment",
                    result.PaymentId,
                    result.RazorpayOrderId,
                    razorpay.KeyId,
                    result.Amount,
                    result.Currency);
                Logger.Info(this.MethodName() + " | Existing checkout order returned. SubscriptionId: {0}, PaymentId: {1}", existingOrder.SubscriptionId, existingOrder.PaymentId);
                return existingOrder;
            }

            if (result.Status != "order_pending")
            {
                // Return the stored state when the payment was already failed, paid, or expired.
                var storedState = new SubscriptionCreatedResponse(result.SubscriptionId, result.Status, result.PaymentId);
                Logger.Debug(this.MethodName() + " | Stored checkout state returned. SubscriptionId: {0}, Status: {1}", storedState.SubscriptionId, storedState.Status);
                return storedState;
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

                var checkout = new SubscriptionCreatedResponse(
                    result.SubscriptionId,
                    "pending_payment",
                    result.PaymentId,
                    orderId,
                    razorpay.KeyId,
                    result.Amount,
                    result.Currency);
                Logger.Info(this.MethodName() + " | Checkout order returned. SubscriptionId: {0}, PaymentId: {1}, OrderId: {2}", checkout.SubscriptionId, checkout.PaymentId, checkout.OrderId);
                return checkout;
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
    public async Task<object?> GetAsync(Guid id, Guid appId)
    {
        try
        {
            // Retrieve one subscription while restricting the query to the application owner.
            var subscription = await repository.GetAsync(id, appId);
            Logger.Debug(this.MethodName() + " | Subscription found: {0}. SubscriptionId: {1}, ApplicationId: {2}", subscription is not null, id, appId);
            return subscription;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, this.MethodName() + " | Service operation failed.");
            throw;
        }
    }

    public async Task<object?> GetByExternalCustomerAsync(Guid appId, string externalId)
    {
        try
        {
            // Retrieve the latest subscription for a customer's external application ID.
            var subscription = await repository.GetByExternalCustomerAsync(appId, externalId);
            Logger.Debug(this.MethodName() + " | Customer subscription found: {0}. ApplicationId: {1}, ExternalId: {2}", subscription is not null, appId, externalId);
            return subscription;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, this.MethodName() + " | Service operation failed.");
            throw;
        }
    }

    public async Task<bool> CancelAsync(Guid id, Guid appId)
    {
        try
        {
            // Set the period-end cancellation flag; this does not remove access immediately.
            var cancelled = await repository.CancelAsync(id, appId);
            Logger.Debug(this.MethodName() + " | Cancellation scheduled: {0}. SubscriptionId: {1}", cancelled, id);
            return cancelled;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, this.MethodName() + " | Service operation failed.");
            throw;
        }
    }

    public async Task<bool> ChangePlanAsync(Guid id, Guid appId, Guid planId)
    {
        try
        {
            // Replace the plan only for an active/trialing subscription; the repository enforces ownership.
            var changed = await repository.ChangePlanAsync(id, appId, planId);
            Logger.Debug(this.MethodName() + " | Plan changed: {0}. SubscriptionId: {1}, PlanId: {2}", changed, id, planId);
            return changed;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, this.MethodName() + " | Service operation failed.");
            throw;
        }
    }
}
