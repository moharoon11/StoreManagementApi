using SubscriptionPlatform.Api.Models;
using SubscriptionPlatform.Api.Repositories;

namespace SubscriptionPlatform.Api.Services;

public sealed class SubscriptionService(SubscriptionRepository repository,RazorpayService razorpay,ILogger<SubscriptionService> logger)
{
    public async Task<SubscriptionCreatedResponse?> CreateAsync(Guid appId,CreateSubscriptionRequest request){if(request.BillingPeriod is not("three_month" or "six_month" or "one_year" or "monthly" or "yearly")||string.IsNullOrWhiteSpace(request.IdempotencyKey)||request.IdempotencyKey.Length>80)throw new ArgumentException("Billing period or idempotency key is invalid.");try{var result=await repository.CreateAsync(appId,request);if(result is null)return null;if(request.StartTrial||result.Status=="trialing")return new(result.SubscriptionId,result.Status);if(result.Status!="order_pending")return new(result.SubscriptionId,result.Status,result.PaymentId);try{var order=razorpay.CreateOrder(result.Amount,result.Currency,result.PaymentId.ToString("N"));await repository.SetOrderAsync(result.PaymentId,order);return new(result.SubscriptionId,result.Status,result.PaymentId,order,razorpay.KeyId,result.Amount,result.Currency);}catch(Exception ex){logger.LogError(ex,"Razorpay order creation failed for payment {PaymentId}",result.PaymentId);await repository.MarkOrderFailedAsync(result.PaymentId);throw;}}catch(Exception ex){logger.LogError(ex,"Subscription creation failed for application {ApplicationId}",appId);throw;}}
    public Task<object?> GetAsync(Guid id,Guid appId)=>repository.GetAsync(id,appId);
    public Task<object?> GetByExternalCustomerAsync(Guid appId,string externalId)=>repository.GetByExternalCustomerAsync(appId,externalId);
    public Task<bool> CancelAsync(Guid id,Guid appId)=>repository.CancelAsync(id,appId);
    public Task<bool> ChangePlanAsync(Guid id,Guid appId,Guid planId)=>repository.ChangePlanAsync(id,appId,planId);
}
