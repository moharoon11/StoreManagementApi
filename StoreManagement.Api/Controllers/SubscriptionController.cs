using Microsoft.AspNetCore.Mvc;
using StoreManagement.Api.Helpers;
using SubscriptionPlatform.Core;
using Microsoft.AspNetCore.Authorization;

namespace StoreManagement.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/subscription")]
public sealed class SubscriptionController(StoreSubscriptionService subscriptions, ILogger<SubscriptionController> logger) : BaseApiController
{
    [HttpGet("options")]
    public async Task<IActionResult> Options()
    {
        try
        {
            var plan = await subscriptions.GetOptionsAsync();
            return plan is null
                ? StatusCode(503, ApiResponse.ErrorResult("Subscription plan is not configured."))
                : Ok(ApiResponse<object>.SuccessResult(plan, "Subscription options loaded."));
        }
        catch (Exception ex) { logger.LogError(ex, "Unable to load subscription options"); return StatusCode(503, ApiResponse.ErrorResult("Subscription service is temporarily unavailable.")); }
    }

    [HttpGet("status")]
    public async Task<IActionResult> Status()
    {
        try
        {
            var status = await subscriptions.GetStatusAsync(CurrentUserId.ToString());
            return Ok(ApiResponse<object>.SuccessResult(status ?? new { has_access = false, status = "none" }, "Subscription status loaded."));
        }
        catch (Exception ex) { logger.LogError(ex, "Unable to load subscription status for user {UserId}", CurrentUserId); return StatusCode(503, ApiResponse.ErrorResult("Subscription service is temporarily unavailable.")); }
    }

    [HttpPost("start")]
    public async Task<IActionResult> Start([FromBody] StartSubscriptionRequest request)
    {
        if (!(request.StartTrial && request.BillingPeriod == "trial") && request.BillingPeriod is not ("three_month" or "six_month" or "one_year"))
            return BadRequest(ApiResponse.ErrorResult("Choose trial, three_month, six_month, or one_year."));
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey) || request.IdempotencyKey.Length > 80)
            return BadRequest(ApiResponse.ErrorResult("A valid idempotency key is required."));
        try
        {
            var result = await subscriptions.StartAsync(CurrentUserId.ToString(), request.StartTrial ? "one_year" : request.BillingPeriod, request.StartTrial, request.IdempotencyKey);
            return Ok(ApiResponse<StoreSubscriptionOrder>.SuccessResult(result, "Checkout initialized."));
        }
        catch (ArgumentException ex) { return BadRequest(ApiResponse.ErrorResult(ex.Message)); }
        catch (InvalidOperationException ex) { return Conflict(ApiResponse.ErrorResult(ex.Message)); }
        catch (Exception ex) { logger.LogError(ex, "Unable to start subscription for user {UserId}", CurrentUserId); return StatusCode(500, ApiResponse.ErrorResult("Unable to initialize checkout.")); }
    }

    [HttpPost("verify")]
    public async Task<IActionResult> Verify([FromBody] VerifySubscriptionPaymentRequest request)
    {
        if (!await subscriptions.VerifyPaymentAsync(request.PaymentId, request.OrderId, request.RazorpayPaymentId, request.Signature))
            return BadRequest(ApiResponse.ErrorResult("Razorpay payment could not be verified. Access was not activated."));
        return Ok(ApiResponse.SuccessResult("Payment captured and verified."));
    }

    [HttpPost("failure")]
    public async Task<IActionResult> Failure([FromBody] FailedSubscriptionPaymentRequest request)
    {
        if (!await subscriptions.MarkPaymentFailedAsync(CurrentUserId.ToString(), request.PaymentId, request.OrderId))
            return Conflict(ApiResponse.ErrorResult("Payment is no longer pending."));
        return Ok(ApiResponse.SuccessResult("Failed checkout recorded; you can retry."));
    }

    [AllowAnonymous]
    [HttpPost("webhook/razorpay")]
    public async Task<IActionResult> RazorpayWebhook()
    {
        using var reader = new StreamReader(Request.Body);
        var payload = await reader.ReadToEndAsync();
        var eventId = Request.Headers["X-Razorpay-Event-Id"].ToString();
        var signature = Request.Headers["X-Razorpay-Signature"].ToString();
        if (string.IsNullOrWhiteSpace(eventId)) return BadRequest(new { error = "missing_event_id" });
        var result = await subscriptions.ProcessWebhookAsync(eventId, payload, signature);
        if (!result.IsValidSignature) return Unauthorized(new { error = "invalid_signature" });
        return Ok(new { accepted = true, duplicate = result.Duplicate });
    }
}

public sealed record StartSubscriptionRequest(string BillingPeriod, bool StartTrial, string IdempotencyKey);
public sealed record VerifySubscriptionPaymentRequest(Guid PaymentId, string OrderId, string RazorpayPaymentId, string Signature);
public sealed record FailedSubscriptionPaymentRequest(Guid PaymentId, string OrderId);
