using System.Text.Json;
using SubscriptionPlatform.Api.Repositories;

namespace SubscriptionPlatform.Api.Services;

public enum WebhookProcessingResult { InvalidSignature, Processed, Duplicate }

public sealed class PaymentService(PaymentRepository repository,RazorpayService razorpay,ILogger<PaymentService> logger)
{
    public async Task<bool> VerifyAsync(Guid appId,Guid paymentId,string orderId,string providerPaymentId,string signature){if(!razorpay.VerifyPayment(orderId,providerPaymentId,signature))return false;try{var provider=razorpay.FetchPayment(providerPaymentId);if(provider.Id!=providerPaymentId||provider.OrderId!=orderId||provider.Status!="captured")return false;return await repository.VerifyAndActivateAsync(appId,paymentId,orderId,providerPaymentId,provider.Amount,provider.Currency);}catch(Exception ex){logger.LogError(ex,"Payment verification processing failed for {PaymentId}",paymentId);throw;}}
    public Task<IEnumerable<object>> HistoryAsync(Guid appId)=>repository.HistoryAsync(appId);
    public Task<bool> MarkFailedAsync(Guid appId,Guid paymentId,string orderId,string externalId)=>repository.MarkFailedAsync(appId,paymentId,orderId,externalId);
    public async Task<WebhookProcessingResult> ProcessWebhookAsync(string eventId,string raw,string signature){if(!razorpay.VerifyWebhook(raw,signature))return WebhookProcessingResult.InvalidSignature;using var doc=JsonDocument.Parse(raw);var root=doc.RootElement;var type=root.GetProperty("event").GetString()??"unknown";string? order=null,payment=null,currency=null,status=null;long? amount=null;if(type is "payment.captured" or "payment.failed"){var entity=root.GetProperty("payload").GetProperty("payment").GetProperty("entity");order=entity.GetProperty("order_id").GetString();payment=entity.GetProperty("id").GetString();currency=entity.GetProperty("currency").GetString();if(entity.TryGetProperty("amount",out var amountElement)&&amountElement.TryGetInt64(out var parsed))amount=parsed;status=entity.GetProperty("status").GetString();if(type=="payment.captured"&&payment is not null){var provider=razorpay.FetchPayment(payment);if(provider.Status!="captured"||provider.OrderId!=order||provider.Amount!=amount||!string.Equals(provider.Currency,currency,StringComparison.OrdinalIgnoreCase))return WebhookProcessingResult.InvalidSignature;}}try{return await repository.ProcessWebhookAsync(eventId,type,order,payment,amount,currency,status)?WebhookProcessingResult.Processed:WebhookProcessingResult.Duplicate;}catch(Exception ex){logger.LogError(ex,"Razorpay webhook processing failed for event {EventId}",eventId);throw;}}
}
