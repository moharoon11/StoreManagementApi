namespace SubscriptionPlatform.Api.Models;
public sealed record LoginRequest(string? ApplicationKey,string? AdminKey);
public sealed record CreateApplicationRequest(string Name);
public sealed record PlanRequest(string Name,string Currency,long MonthlyAmountMinor,long YearlyAmountMinor,int TrialDays,string FeaturesJson="{}",string LimitsJson="{}",long ThreeMonthAmountMinor=0,long SixMonthAmountMinor=0);
public sealed record CustomerRequest(string ExternalId,string? Email=null);
public sealed record CreateSubscriptionRequest(Guid CustomerId,Guid PlanId,string BillingPeriod,string IdempotencyKey,bool StartTrial=false);
public sealed record VerifyPaymentRequest(Guid PaymentId,string OrderId,string RazorpayPaymentId,string Signature);
public sealed record FailedPaymentRequest(Guid PaymentId,string OrderId,string ExternalCustomerId);
public sealed record SubscriptionCreatedResponse(Guid SubscriptionId,string Status,Guid? PaymentId=null,string? OrderId=null,string? KeyId=null,long? AmountMinor=null,string? Currency=null);
