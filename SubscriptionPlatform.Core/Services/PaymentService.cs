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
        Logger.Debug($"{nameof(VerifyAsync)} | ApplicationId: {appId} | Payload: PaymentId={paymentId}, OrderId={orderId}");
        if (!razorpay.VerifyPayment(orderId, providerPaymentId, signature))
        {
            Logger.Warn($"{nameof(VerifyAsync)} | PaymentId: {paymentId} | OrderId: {orderId} | Provider signature verification failed");
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
            Logger.Error(ex, $"{nameof(VerifyAsync)} | PaymentId: {paymentId} | OrderId: {orderId} | Exception: {ex.Message}");
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
        Logger.Debug($"{nameof(ProcessWebhookAsync)} | EventId: {eventId} | PayloadLength: {raw.Length}");
        if (!razorpay.VerifyWebhook(raw, signature))
        {
            Logger.Warn($"{nameof(ProcessWebhookAsync)} | EventId: {eventId} | Provider signature verification failed");
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
            Logger.Error(ex, $"{nameof(ProcessWebhookAsync)} | EventId: {eventId} | Exception: {ex.Message}");
            throw;
        }
    }
}
