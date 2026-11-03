using System.Text.Json;
using SubscriptionPlatform.Api.Repositories;
using NLog;

namespace SubscriptionPlatform.Api.Services;

public enum WebhookProcessingResult
{
    InvalidSignature,
    Processed,
    Duplicate
}

public sealed class PaymentService(PaymentRepository repository, RazorpayService razorpay)
{
    private static readonly Logger Logger = LogManager.GetLogger("SubscriptionPlatform.Services.PaymentService");
    public async Task<bool> VerifyAsync(
        Guid appId,
        Guid paymentId,
        string orderId,
        string providerPaymentId,
        string signature)
    {
        // Verify the checkout callback before allowing the payment to activate access.
        Logger.Debug($"{this.MethodName()} | ApplicationId: {appId} | Payload: PaymentId={paymentId}, OrderId={orderId}");
        if (!razorpay.VerifyPayment(orderId, providerPaymentId, signature))
        {
            // The signature proves that the callback values came from Razorpay.
            Logger.Warn($"{this.MethodName()} | PaymentId: {paymentId} | OrderId: {orderId} | Provider signature verification failed");
            return false;
        }

        try
        {
            // Ask Razorpay for the authoritative payment state instead of trusting client-supplied values.
            var provider = razorpay.FetchPayment(providerPaymentId);
            if (provider.Id != providerPaymentId
                || provider.OrderId != orderId
                || provider.Status != "captured")
            {
                // A payment is usable only when its order, ID, and captured status all match.
                Logger.Warn(this.MethodName() + " | Provider payment details did not match the checkout. PaymentId: {0}, OrderId: {1}, ProviderStatus: {2}", paymentId, orderId, provider.Status);
                return false;
            }

            // Match the provider payment to the local row, then mark payment paid and subscription active.
            var activated = await repository.VerifyAndActivateAsync(
                appId,
                paymentId,
                orderId,
                providerPaymentId,
                provider.Amount,
                provider.Currency);
            if (activated)
                Logger.Info(this.MethodName() + " | Payment verified and subscription activated. PaymentId: {0}", paymentId);
            else
                Logger.Warn(this.MethodName() + " | Payment verified by provider but local activation was rejected. PaymentId: {0}", paymentId);
            return activated;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, $"{this.MethodName()} | PaymentId: {paymentId} | OrderId: {orderId} | Exception: {ex.Message}");
            throw;
        }
    }

    public async Task<IEnumerable<object>> HistoryAsync(Guid appId)
    {
        try
        {
            // Return payment history limited to subscriptions owned by this application.
            var payments = (await repository.HistoryAsync(appId)).ToList();
            Logger.Debug(this.MethodName() + " | Payment history returned. ApplicationId: {0}, Count: {1}", appId, payments.Count);
            return payments;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, this.MethodName() + " | Service operation failed.");
            throw;
        }
    }

    public async Task<bool> MarkFailedAsync(
        Guid appId,
        Guid paymentId,
        string orderId,
        string externalId)
    {
        try
        {
            // Mark a pending payment as failed after confirming its customer and order ownership.
            var marked = await repository.MarkFailedAsync(appId, paymentId, orderId, externalId);
            if (marked)
                Logger.Info(this.MethodName() + " | Payment failure callback recorded. PaymentId: {0}", paymentId);
            else
                Logger.Warn(this.MethodName() + " | Payment failure callback ignored. PaymentId: {0}", paymentId);
            return marked;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, this.MethodName() + " | Service operation failed.");
            throw;
        }
    }

    public async Task<WebhookProcessingResult> ProcessWebhookAsync(
        string eventId,
        string raw,
        string signature)
    {
        // Webhooks can arrive without a logged-in user, so the provider signature is their authentication.
        Logger.Debug($"{this.MethodName()} | EventId: {eventId} | PayloadLength: {raw.Length}");
        if (!razorpay.VerifyWebhook(raw, signature))
        {
            // Never parse or apply an event that was not signed by Razorpay.
            Logger.Warn($"{this.MethodName()} | EventId: {eventId} | Provider signature verification failed");
            return WebhookProcessingResult.InvalidSignature;
        }

        // Read the event name first; only payment events contain data that changes subscriptions.
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(raw);
        }
        catch (JsonException ex)
        {
            Logger.Error(ex, this.MethodName() + " | Failed to parse signed webhook JSON. EventId: {0}, PayloadLength: {1}", eventId, raw.Length);
            throw;
        }
        using var parsedDocument = document;
        var root = document.RootElement;
        var eventType = root.GetProperty("event").GetString() ?? "unknown";
        string? orderId = null;
        string? paymentId = null;
        string? currency = null;
        string? status = null;
        long? amount = null;

        if (eventType is "payment.captured" or "payment.failed")
        {
            // Extract the provider order, payment, amount, currency, and status for reconciliation.
            var entity = root
                .GetProperty("payload")
                .GetProperty("payment")
                .GetProperty("entity");
            orderId = entity.GetProperty("order_id").GetString();
            paymentId = entity.GetProperty("id").GetString();
            currency = entity.GetProperty("currency").GetString();
            if (entity.TryGetProperty("amount", out var amountElement)
                && amountElement.TryGetInt64(out var parsedAmount))
            {
                amount = parsedAmount;
            }

            status = entity.GetProperty("status").GetString();
            if (eventType == "payment.captured" && paymentId is not null)
            {
                // Confirm captured details with Razorpay before treating the webhook as proof of payment.
                var provider = razorpay.FetchPayment(paymentId);
                if (provider.Status != "captured"
                    || provider.OrderId != orderId
                    || provider.Amount != amount
                    || !string.Equals(provider.Currency, currency, StringComparison.OrdinalIgnoreCase))
                {
                    // Do not reconcile a webhook whose details differ from the provider's payment record.
                    Logger.Warn(this.MethodName() + " | Webhook payment details did not match provider record. EventId: {0}, OrderId: {1}", eventId, orderId);
                    return WebhookProcessingResult.InvalidSignature;
                }
            }
        }

        try
        {
            // Store the event and update its payment once; repeated event IDs are ignored by the repository.
            var processed = await repository.ProcessWebhookAsync(
                eventId,
                eventType,
                orderId,
                paymentId,
                amount,
                currency,
                status);
            Logger.Info(this.MethodName() + " | Payment webhook {0}. EventId: {1}, EventType: {2}", processed ? "processed" : "already handled", eventId, eventType);
            return processed
                ? WebhookProcessingResult.Processed
                : WebhookProcessingResult.Duplicate;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, $"{this.MethodName()} | EventId: {eventId} | Exception: {ex.Message}");
            throw;
        }
    }
}
