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
        Logger.Debug($"{nameof(VerifyAsync)} | ApplicationId: {appId} | Payload: PaymentId={paymentId}, OrderId={orderId}");
        if (!razorpay.VerifyPayment(orderId, providerPaymentId, signature))
        {
            // The signature proves that the callback values came from Razorpay.
            Logger.Warn($"{nameof(VerifyAsync)} | PaymentId: {paymentId} | OrderId: {orderId} | Provider signature verification failed");
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
                Logger.Info("Payment verified and subscription activated. PaymentId: {0}", paymentId);
            else
                Logger.Warn("Payment verified by provider but local activation was rejected. PaymentId: {0}", paymentId);
            return activated;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, $"{nameof(VerifyAsync)} | PaymentId: {paymentId} | OrderId: {orderId} | Exception: {ex.Message}");
            throw;
        }
    }

    public Task<IEnumerable<object>> HistoryAsync(Guid appId)
    {
        // Return payment history limited to subscriptions owned by this application.
        return repository.HistoryAsync(appId);
    }

    public async Task<bool> MarkFailedAsync(
        Guid appId,
        Guid paymentId,
        string orderId,
        string externalId)
    {
        // Mark a pending payment as failed after confirming its customer and order ownership.
        var marked = await repository.MarkFailedAsync(appId, paymentId, orderId, externalId);
        if (marked)
            Logger.Info("Payment failure callback recorded. PaymentId: {0}", paymentId);
        else
            Logger.Warn("Payment failure callback ignored. PaymentId: {0}", paymentId);
        return marked;
    }

    public async Task<WebhookProcessingResult> ProcessWebhookAsync(
        string eventId,
        string raw,
        string signature)
    {
        // Webhooks can arrive without a logged-in user, so the provider signature is their authentication.
        Logger.Debug($"{nameof(ProcessWebhookAsync)} | EventId: {eventId} | PayloadLength: {raw.Length}");
        if (!razorpay.VerifyWebhook(raw, signature))
        {
            // Never parse or apply an event that was not signed by Razorpay.
            Logger.Warn($"{nameof(ProcessWebhookAsync)} | EventId: {eventId} | Provider signature verification failed");
            return WebhookProcessingResult.InvalidSignature;
        }

        // Read the event name first; only payment events contain data that changes subscriptions.
        using var document = JsonDocument.Parse(raw);
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
            Logger.Info("Payment webhook {0}. EventId: {1}, EventType: {2}", processed ? "processed" : "already handled", eventId, eventType);
            return processed
                ? WebhookProcessingResult.Processed
                : WebhookProcessingResult.Duplicate;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, $"{nameof(ProcessWebhookAsync)} | EventId: {eventId} | Exception: {ex.Message}");
            throw;
        }
    }
}
