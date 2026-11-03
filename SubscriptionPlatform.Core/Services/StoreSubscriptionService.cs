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
            // Read and validate the application ID used to isolate Store subscription data.
            if (Guid.TryParse(configuration["SubscriptionPlatform:ApplicationId"], out var id))
            {
                return id;
            }

            Logger.Error(this.MethodName() + " | SubscriptionPlatform:ApplicationId is missing or invalid.");
            throw new InvalidOperationException(
                "SubscriptionPlatform:ApplicationId must be configured.");
        }
    }

    private Guid PlanId
    {
        get
        {
            // Read and validate the plan ID configured for the Store application.
            if (Guid.TryParse(configuration["SubscriptionPlatform:PlanId"], out var id))
            {
                return id;
            }

            Logger.Error(this.MethodName() + " | SubscriptionPlatform:PlanId is missing or invalid.");
            throw new InvalidOperationException(
                "SubscriptionPlatform:PlanId must be configured.");
        }
    }

    public async Task<object?> GetOptionsAsync()
    {
        // Return the configured plan so the client can display prices and trial options.
        Logger.Debug($"{this.MethodName()} | ApplicationId: {ApplicationId} | PlanId: {PlanId}");
        var plan = await catalog.GetPlanAsync(ApplicationId, PlanId);
        Logger.Debug(this.MethodName() + " | Configured plan found: {0}", plan is not null);
        return plan;
    }

    public async Task<object?> GetStatusAsync(string externalCustomerId)
    {
        // Return the subscription status belonging to the signed-in Store customer.
        Logger.Debug($"{this.MethodName()} | ApplicationId: {ApplicationId} | ExternalCustomerId: {externalCustomerId}");
        var status = await subscriptions.GetByExternalCustomerAsync(ApplicationId, externalCustomerId);
        Logger.Debug(this.MethodName() + " | Customer subscription found: {0}. ExternalCustomerId: {1}", status is not null, externalCustomerId);
        return status;
    }

    public async Task<StoreSubscriptionOrder> StartAsync(
        string externalCustomerId,
        string billingPeriod,
        bool startTrial,
        string idempotencyKey)
    {
        // This facade adapts the Store API request to the shared subscription platform.
        Logger.Info($"{this.MethodName()} | ApplicationId: {ApplicationId} | ExternalCustomerId: {externalCustomerId} | " +
            $"Payload: BillingPeriod={billingPeriod}, StartTrial={startTrial}");
        // The Store user ID is converted into the platform customer record used by subscriptions.
        dynamic customer = await catalog.CreateCustomerAsync(ApplicationId, new CustomerRequest(externalCustomerId));
        // Use the configured plan so the Store client cannot choose another application's plan.
        var request = new CreateSubscriptionRequest(
            (Guid)customer.id,
            PlanId,
            billingPeriod,
            idempotencyKey,
            startTrial);
        // Create the trial or paid subscription, including the local payment row for paid terms.
        var result = await subscriptions.CreateAsync(ApplicationId, request)
            ?? throw new InvalidOperationException("Configured plan or customer is unavailable.");
        // Return the IDs and server-calculated payment values needed to open Razorpay Checkout.
        var order = new StoreSubscriptionOrder(
            result.SubscriptionId,
            result.Status,
            result.PaymentId,
            result.OrderId,
            result.KeyId,
            result.AmountMinor,
            result.Currency);
        Logger.Info(this.MethodName() + " | Subscription start returned. SubscriptionId: {0}, Status: {1}, PaymentId: {2}", order.SubscriptionId, order.Status, order.PaymentId);
        return order;
    }

    public async Task<bool> VerifyPaymentAsync(
        Guid paymentId,
        string orderId,
        string razorpayPaymentId,
        string signature)
    {
        // A successful client callback is not trusted by itself; this verifies the provider payment first.
        Logger.Debug($"{this.MethodName()} | ApplicationId: {ApplicationId} | Payload: PaymentId={paymentId}, OrderId={orderId}");
        var verified = await payments.VerifyAsync(
            ApplicationId,
            paymentId,
            orderId,
            razorpayPaymentId,
            signature);
        Logger.Debug(this.MethodName() + " | Payment verification result: {0}. PaymentId: {1}", verified, paymentId);
        return verified;
    }

    public async Task<bool> MarkPaymentFailedAsync(
        string externalCustomerId,
        Guid paymentId,
        string orderId)
    {
        // The customer ID is passed through so a user cannot mark another user's payment as failed.
        Logger.Info($"{this.MethodName()} | ApplicationId: {ApplicationId} | Payload: PaymentId={paymentId}, OrderId={orderId}");
        var marked = await payments.MarkFailedAsync(
            ApplicationId,
            paymentId,
            orderId,
            externalCustomerId);
        Logger.Debug(this.MethodName() + " | Payment failure recorded: {0}. PaymentId: {1}", marked, paymentId);
        return marked;
    }

    public async Task<(bool IsValidSignature, bool Duplicate)> ProcessWebhookAsync(string eventId, string rawPayload, string signature)
    {
        // Keep the raw body unchanged because Razorpay's signature is calculated from those exact bytes.
        Logger.Debug($"{this.MethodName()} | EventId: {eventId} | PayloadLength: {rawPayload.Length}");
        var result = await payments.ProcessWebhookAsync(eventId, rawPayload, signature);
        Logger.Debug(this.MethodName() + " | Webhook result: {0}. EventId: {1}", result, eventId);
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
