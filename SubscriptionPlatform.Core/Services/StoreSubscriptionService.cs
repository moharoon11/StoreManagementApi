using SubscriptionPlatform.Api.Models;
using SubscriptionPlatform.Api.Services;
using NLog;

namespace SubscriptionPlatform.Core;

/// <summary>In-process subscription facade for applications that reference SubscriptionPlatform.Core.</summary>
public sealed class StoreSubscriptionService(
    CatalogService catalog,
    SubscriptionService subscriptions,
    PaymentService payments,
    IConfiguration configuration)
{
    private static readonly Logger Logger = LogManager.GetLogger("SubscriptionPlatform.Services.StoreSubscriptionService");
    private Guid ApplicationId
    {
        get
        {
            if (Guid.TryParse(configuration["SubscriptionPlatform:ApplicationId"], out var id))
            {
                return id;
            }

            throw new InvalidOperationException(
                "SubscriptionPlatform:ApplicationId must be configured.");
        }
    }

    private Guid PlanId
    {
        get
        {
            if (Guid.TryParse(configuration["SubscriptionPlatform:PlanId"], out var id))
            {
                return id;
            }

            throw new InvalidOperationException(
                "SubscriptionPlatform:PlanId must be configured.");
        }
    }

    public Task<object?> GetOptionsAsync()
    {
        Logger.Debug($"{nameof(GetOptionsAsync)} | ApplicationId: {ApplicationId} | PlanId: {PlanId}");
        return catalog.GetPlanAsync(ApplicationId, PlanId);
    }

    public Task<object?> GetStatusAsync(string externalCustomerId)
    {
        Logger.Debug($"{nameof(GetStatusAsync)} | ApplicationId: {ApplicationId} | ExternalCustomerId: {externalCustomerId}");
        return subscriptions.GetByExternalCustomerAsync(ApplicationId, externalCustomerId);
    }

    public async Task<StoreSubscriptionOrder> StartAsync(
        string externalCustomerId,
        string billingPeriod,
        bool startTrial,
        string idempotencyKey)
    {
        Logger.Info($"{nameof(StartAsync)} | ApplicationId: {ApplicationId} | ExternalCustomerId: {externalCustomerId} | " +
            $"Payload: BillingPeriod={billingPeriod}, StartTrial={startTrial}");
        dynamic customer = await catalog.CreateCustomerAsync(ApplicationId, new CustomerRequest(externalCustomerId));
        var request = new CreateSubscriptionRequest(
            (Guid)customer.id,
            PlanId,
            billingPeriod,
            idempotencyKey,
            startTrial);
        var result = await subscriptions.CreateAsync(ApplicationId, request)
            ?? throw new InvalidOperationException("Configured plan or customer is unavailable.");
        return new StoreSubscriptionOrder(
            result.SubscriptionId,
            result.Status,
            result.PaymentId,
            result.OrderId,
            result.KeyId,
            result.AmountMinor,
            result.Currency);
    }

    public Task<bool> VerifyPaymentAsync(
        Guid paymentId,
        string orderId,
        string razorpayPaymentId,
        string signature)
    {
        Logger.Debug($"{nameof(VerifyPaymentAsync)} | ApplicationId: {ApplicationId} | Payload: PaymentId={paymentId}, OrderId={orderId}");
        return payments.VerifyAsync(
            ApplicationId,
            paymentId,
            orderId,
            razorpayPaymentId,
            signature);
    }

    public Task<bool> MarkPaymentFailedAsync(
        string externalCustomerId,
        Guid paymentId,
        string orderId)
    {
        Logger.Info($"{nameof(MarkPaymentFailedAsync)} | ApplicationId: {ApplicationId} | Payload: PaymentId={paymentId}, OrderId={orderId}");
        return payments.MarkFailedAsync(
            ApplicationId,
            paymentId,
            orderId,
            externalCustomerId);
    }

    public async Task<(bool IsValidSignature, bool Duplicate)> ProcessWebhookAsync(string eventId, string rawPayload, string signature)
    {
        Logger.Debug($"{nameof(ProcessWebhookAsync)} | EventId: {eventId} | PayloadLength: {rawPayload.Length}");
        var result = await payments.ProcessWebhookAsync(eventId, rawPayload, signature);
        return (result != WebhookProcessingResult.InvalidSignature, result == WebhookProcessingResult.Duplicate);
    }
}

public sealed record StoreSubscriptionOrder(
    Guid SubscriptionId,
    string Status,
    Guid? PaymentId,
    string? OrderId,
    string? KeyId,
    long? AmountMinor,
    string? Currency);
