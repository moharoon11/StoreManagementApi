namespace SubscriptionPlatform.Api.Models;

public sealed record LoginRequest(
    string? ApplicationKey,
    string? AdminKey);

public sealed record CreateApplicationRequest(string Name);

public sealed record PlanRequest(
    string Name,
    string Currency,
    int TrialDays,
    string FeaturesJson = "{}",
    string LimitsJson = "{}",
    IReadOnlyList<PlanOptionRequest>? PricingOptions = null);

public sealed record PlanOptionRequest(
    int? Id,
    string Code,
    string DisplayName,
    long AmountMinor,
    string Currency,
    int DurationSeconds = 0,
    bool Active = true,
    int SortOrder = 0,
    int DurationDays = 0);

public sealed record CustomerRequest(
    string ExternalId,
    string? Email = null);

public sealed record CreateSubscriptionRequest(
    Guid CustomerId,
    Guid PlanId,
    string BillingPeriod,
    string IdempotencyKey,
    bool StartTrial = false,
    int? PlanOptionId = null);

public sealed record VerifyPaymentRequest(
    Guid PaymentId,
    string OrderId,
    string RazorpayPaymentId,
    string Signature);

public sealed record FailedPaymentRequest(
    Guid PaymentId,
    string OrderId,
    string ExternalCustomerId);

public sealed record SubscriptionCreatedResponse(
    Guid SubscriptionId,
    string Status,
    Guid? PaymentId = null,
    string? OrderId = null,
    string? KeyId = null,
    long? AmountMinor = null,
    string? Currency = null);
