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
        Logger.Debug($"{nameof(CreateAsync)} | ApplicationId: {appId} | CustomerId: {request.CustomerId} | PlanId: {request.PlanId} | " +
            $"Payload: BillingPeriod={request.BillingPeriod}, StartTrial={request.StartTrial}");
        if (request.BillingPeriod is not ("three_month" or "six_month" or "one_year" or "monthly" or "yearly")
            || string.IsNullOrWhiteSpace(request.IdempotencyKey)
            || request.IdempotencyKey.Length > 80)
        {
            throw new ArgumentException("Billing period or idempotency key is invalid.");
        }

        try
        {
            var result = await repository.CreateAsync(appId, request);

            if (result is null)
            {
                Logger.Warn($"{nameof(CreateAsync)} | ApplicationId: {appId} | CustomerId: {request.CustomerId} | Payload: no result returned");
                return null;
            }

            if (request.StartTrial || result.Status == "trialing")
            {
                Logger.Info($"{nameof(CreateAsync)} | ApplicationId: {appId} | SubscriptionId: {result.SubscriptionId} | Status: {result.Status}");
                return new(result.SubscriptionId, result.Status);
            }

            // An idempotent retry must return the existing order instead of
            // creating a second Razorpay order.
            if (!string.IsNullOrWhiteSpace(result.RazorpayOrderId))
            {
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
                return new(result.SubscriptionId, result.Status, result.PaymentId);
            }

            try
            {
                var orderId = razorpay.CreateOrder(
                    result.Amount,
                    result.Currency,
                    result.PaymentId.ToString("N"));
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
                Logger.Error(ex, $"{nameof(CreateAsync)} | Razorpay order creation failed | PaymentId: {result.PaymentId}");
                await repository.MarkOrderFailedAsync(result.PaymentId);
                throw;
            }
        }
        catch (Exception ex)
        {
            Logger.Error(ex, $"{nameof(CreateAsync)} | ApplicationId: {appId} | CustomerId: {request.CustomerId} | Exception: {ex.Message}");
            throw;
        }
    }
    public Task<object?> GetAsync(Guid id, Guid appId)
    {
        return repository.GetAsync(id, appId);
    }

    public Task<object?> GetByExternalCustomerAsync(Guid appId, string externalId)
    {
        return repository.GetByExternalCustomerAsync(appId, externalId);
    }

    public Task<bool> CancelAsync(Guid id, Guid appId)
    {
        return repository.CancelAsync(id, appId);
    }

    public Task<bool> ChangePlanAsync(Guid id, Guid appId, Guid planId)
    {
        return repository.ChangePlanAsync(id, appId, planId);
    }
}
