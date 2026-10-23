using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StoreManagement.Api.Helpers;
using StoreManagement.Api.Services;

namespace StoreManagement.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/subscription")]
public sealed class SubscriptionController(SubscriptionPlatformClient platform, ILogger<SubscriptionController> logger) : BaseApiController
{
    [HttpGet("options")]
    public async Task<IActionResult> Options(CancellationToken ct)
    {
        try
        {
            using var plans = await platform.GetPlansAsync(ct);
            var id = Guid.Parse(HttpContext.RequestServices.GetRequiredService<IConfiguration>()["SubscriptionPlatform:PlanId"]!);
            var plan = plans.RootElement.EnumerateArray().FirstOrDefault(p => p.GetProperty("id").GetGuid() == id);
            if (plan.ValueKind == System.Text.Json.JsonValueKind.Undefined) return StatusCode(503, ApiResponse.ErrorResult("Subscription plan is not configured."));
            return Ok(ApiResponse<System.Text.Json.JsonElement>.SuccessResult(plan, "Subscription options loaded."));
        }
        catch (Exception ex) { logger.LogError(ex, "Unable to load subscription options"); return StatusCode(503, ApiResponse.ErrorResult("Subscription service is temporarily unavailable.")); }
    }

    [HttpGet("status")]
    public async Task<IActionResult> Status(CancellationToken ct)
    {
        try { using var status = await platform.GetStatusAsync(CurrentUserId, ct); return Ok(ApiResponse<System.Text.Json.JsonElement?>.SuccessResult(status.RootElement.ValueKind == System.Text.Json.JsonValueKind.Null ? null : status.RootElement.Clone(), "Subscription status loaded.")); }
        catch (Exception ex) { logger.LogError(ex, "Unable to load subscription status for user {UserId}", CurrentUserId); return StatusCode(503, ApiResponse.ErrorResult("Subscription service is temporarily unavailable.")); }
    }

    [HttpPost("start")]
    public async Task<IActionResult> Start([FromBody] StartSubscriptionRequest request, CancellationToken ct)
    {
        if (!(request.StartTrial && request.BillingPeriod == "trial") && request.BillingPeriod is not ("three_month" or "six_month" or "one_year")) return BadRequest(ApiResponse.ErrorResult("Choose trial, three_month, six_month, or one_year."));
        try { using var result = await platform.StartAsync(CurrentUserId, request.StartTrial ? "one_year" : request.BillingPeriod, request.StartTrial, request.IdempotencyKey, ct); return Ok(ApiResponse<System.Text.Json.JsonElement>.SuccessResult(result.RootElement.Clone(), "Checkout initialized.")); }
        catch (InvalidOperationException ex) { return Conflict(ApiResponse.ErrorResult(ex.Message)); }
        catch (HttpRequestException ex) { logger.LogError(ex, "Subscription platform request failed for user {UserId}", CurrentUserId); return StatusCode(503, ApiResponse.ErrorResult("Subscription service is temporarily unavailable.")); }
        catch (Exception ex) { logger.LogError(ex, "Unable to start subscription for user {UserId}", CurrentUserId); return StatusCode(500, ApiResponse.ErrorResult("Unable to initialize checkout.")); }
    }

    [HttpPost("verify")]
    public async Task<IActionResult> Verify([FromBody] VerifySubscriptionPaymentRequest request, CancellationToken ct)
    {
        if (!await platform.VerifyAsync(request.PaymentId, request.OrderId, request.RazorpayPaymentId, request.Signature, ct)) return BadRequest(ApiResponse.ErrorResult("Razorpay payment could not be verified. Access was not activated."));
        return Ok(ApiResponse.SuccessResult("Payment captured and verified."));
    }

    [HttpPost("failure")]
    public async Task<IActionResult> Failure([FromBody] FailedSubscriptionPaymentRequest request, CancellationToken ct)
    {
        if (!await platform.MarkFailedAsync(CurrentUserId, request.PaymentId, request.OrderId, ct)) return Conflict(ApiResponse.ErrorResult("Payment is no longer pending."));
        return Ok(ApiResponse.SuccessResult("Failed checkout recorded; you can retry."));
    }
}

public sealed record StartSubscriptionRequest(string BillingPeriod, bool StartTrial, string IdempotencyKey);
public sealed record VerifySubscriptionPaymentRequest(Guid PaymentId, string OrderId, string RazorpayPaymentId, string Signature);
public sealed record FailedSubscriptionPaymentRequest(Guid PaymentId, string OrderId);
