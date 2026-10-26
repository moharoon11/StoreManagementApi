using System.Text.Json;
using SubscriptionPlatform.Api.Repositories;

namespace SubscriptionPlatform.Api.Services;

public enum WebhookProcessingResult
{
    InvalidSignature,
    Processed,
    Duplicate
}

public sealed class PaymentService(PaymentRepository repository, RazorpayService razorpay, ILogger<PaymentService> logger)
{
    public async Task<bool> VerifyAsync(
        Guid appId,
        Guid paymentId,
        string orderId,
        string providerPaymentId,
        string signature)
    {
        if (!razorpay.VerifyPayment(orderId, providerPaymentId, signature))
        {
            return false;
        }

        try
        {
            var provider = razorpay.FetchPayment(providerPaymentId);
            if (provider.Id != providerPaymentId
                || provider.OrderId != orderId
                || provider.Status != "captured")
            {
                return false;
            }

            return await repository.VerifyAndActivateAsync(
                appId,
                paymentId,
                orderId,
                providerPaymentId,
                provider.Amount,
                provider.Currency);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Payment verification processing failed for {PaymentId}", paymentId);
            throw;
        }
    }

    public Task<IEnumerable<object>> HistoryAsync(Guid appId)
    {
        return repository.HistoryAsync(appId);
    }

    public Task<bool> MarkFailedAsync(
        Guid appId,
        Guid paymentId,
        string orderId,
        string externalId)
    {
        return repository.MarkFailedAsync(appId, paymentId, orderId, externalId);
    }

    public async Task<WebhookProcessingResult> ProcessWebhookAsync(
        string eventId,
        string raw,
        string signature)
    {
        if (!razorpay.VerifyWebhook(raw, signature))
        {
            return WebhookProcessingResult.InvalidSignature;
        }

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
                var provider = razorpay.FetchPayment(paymentId);
                if (provider.Status != "captured"
                    || provider.OrderId != orderId
                    || provider.Amount != amount
                    || !string.Equals(provider.Currency, currency, StringComparison.OrdinalIgnoreCase))
                {
                    return WebhookProcessingResult.InvalidSignature;
                }
            }
        }

        try
        {
            var processed = await repository.ProcessWebhookAsync(
                eventId,
                eventType,
                orderId,
                paymentId,
                amount,
                currency,
                status);
            return processed
                ? WebhookProcessingResult.Processed
                : WebhookProcessingResult.Duplicate;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Razorpay webhook processing failed for event {EventId}", eventId);
            throw;
        }
    }
}
