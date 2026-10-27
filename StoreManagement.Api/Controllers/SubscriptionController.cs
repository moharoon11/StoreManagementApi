using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc;
using NLog;
using StoreManagement.Api.Helpers;
using SubscriptionPlatform.Core;
using Microsoft.AspNetCore.Authorization;

namespace StoreManagement.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/subscription")]
public sealed class SubscriptionController(StoreSubscriptionService subscriptions) : BaseApiController
{
    private static readonly Logger Logger = LogManager.GetLogger("SubscriptionController");

    [HttpGet("options")]
    public async Task<IActionResult> Options()
    {
        try
        {
            var plan = await subscriptions.GetOptionsAsync();
            Logger.Info($"{nameof(Options)} | RequestId: {HttpContext.TraceIdentifier} | UserId: {CurrentUserId} | " +
                $"Payload: none | HasPlan: {plan is not null}");
            return plan is null
                ? StatusCode(503, ApiResponse.ErrorResult("Subscription plan is not configured."))
                : Ok(ApiResponse<object>.SuccessResult(plan, "Subscription options loaded."));
        }
        catch (Exception ex)
        {
            Logger.Error(ex, $"{nameof(Options)} | RequestId: {HttpContext.TraceIdentifier} | UserId: {CurrentUserId} | " +
                $"Payload: none | Exception: {ex.Message}");
            return StatusCode(503, ApiResponse.ErrorResult("Subscription service is temporarily unavailable."));
        }
    }

    [HttpGet("status")]
    public async Task<IActionResult> Status()
    {
        try
        {
            var status = await subscriptions.GetStatusAsync(CurrentUserId.ToString());
            Logger.Info($"{nameof(Status)} | RequestId: {HttpContext.TraceIdentifier} | UserId: {CurrentUserId} | Payload: none");
            return Ok(ApiResponse<object>.SuccessResult(status ?? new { has_access = false, status = "none" }, "Subscription status loaded."));
        }
        catch (Exception ex)
        {
            Logger.Error(ex, $"{nameof(Status)} | RequestId: {HttpContext.TraceIdentifier} | UserId: {CurrentUserId} | " +
                $"Payload: none | Exception: {ex.Message}");
            return StatusCode(503, ApiResponse.ErrorResult("Subscription service is temporarily unavailable."));
        }
    }

    [HttpPost("start")]
    public async Task<IActionResult> Start([FromBody] StartSubscriptionRequest request)
    {
        if (!(request.StartTrial && request.BillingPeriod == "trial") && request.BillingPeriod is not ("three_month" or "six_month" or "one_year"))
        {
            Logger.Warn($"{nameof(Start)} | RequestId: {HttpContext.TraceIdentifier} | UserId: {CurrentUserId} | " +
                $"Payload: {SerializeSafePayload(request)} | Invalid billing period");
            return BadRequest(ApiResponse.ErrorResult("Choose trial, three_month, six_month, or one_year."));
        }
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey) || request.IdempotencyKey.Length > 80)
        {
            Logger.Warn($"{nameof(Start)} | RequestId: {HttpContext.TraceIdentifier} | UserId: {CurrentUserId} | " +
                $"Payload: {SerializeSafePayload(new { request.BillingPeriod, request.StartTrial, request.IdempotencyKey })} | Invalid idempotency key");
            return BadRequest(ApiResponse.ErrorResult("A valid idempotency key is required."));
        }
        try
        {
            var result = await subscriptions.StartAsync(CurrentUserId.ToString(), request.StartTrial ? "one_year" : request.BillingPeriod, request.StartTrial, request.IdempotencyKey);
            Logger.Info($"{nameof(Start)} | RequestId: {HttpContext.TraceIdentifier} | UserId: {CurrentUserId} | " +
                $"Payload: {SerializeSafePayload(request)} | SubscriptionId: {result.SubscriptionId} | Status: {result.Status}");
            return Ok(ApiResponse<StoreSubscriptionOrder>.SuccessResult(result, "Checkout initialized."));
        }
        catch (ArgumentException ex)
        {
            Logger.Warn($"{nameof(Start)} | RequestId: {HttpContext.TraceIdentifier} | UserId: {CurrentUserId} | " +
                $"Payload: {SerializeSafePayload(request)} | Exception: {ex.Message}");
            return BadRequest(ApiResponse.ErrorResult(ex.Message));
        }
        catch (InvalidOperationException ex)
        {
            Logger.Warn($"{nameof(Start)} | RequestId: {HttpContext.TraceIdentifier} | UserId: {CurrentUserId} | " +
                $"Payload: {SerializeSafePayload(request)} | Exception: {ex.Message}");
            return Conflict(ApiResponse.ErrorResult(ex.Message));
        }
        catch (Exception ex)
        {
            Logger.Error(ex, $"{nameof(Start)} | RequestId: {HttpContext.TraceIdentifier} | UserId: {CurrentUserId} | " +
                $"Payload: {SerializeSafePayload(request)} | Exception: {ex.Message}");
            return StatusCode(500, ApiResponse.ErrorResult("Unable to initialize checkout."));
        }
    }

    [HttpPost("verify")]
    public async Task<IActionResult> Verify([FromBody] VerifySubscriptionPaymentRequest request)
    {
        try
        {
            var verified = await subscriptions.VerifyPaymentAsync(request.PaymentId, request.OrderId, request.RazorpayPaymentId, request.Signature);
            Logger.Info($"{nameof(Verify)} | RequestId: {HttpContext.TraceIdentifier} | UserId: {CurrentUserId} | " +
                $"Payload: {JsonSerializer.Serialize(new { request.PaymentId, request.OrderId })} | Verified: {verified}");
            return verified
                ? Ok(ApiResponse.SuccessResult("Payment captured and verified."))
                : BadRequest(ApiResponse.ErrorResult("Razorpay payment could not be verified. Access was not activated."));
        }
        catch (Exception ex)
        {
            Logger.Error(ex, $"{nameof(Verify)} | RequestId: {HttpContext.TraceIdentifier} | UserId: {CurrentUserId} | " +
                $"Payload: {JsonSerializer.Serialize(new { request.PaymentId, request.OrderId })} | Exception: {ex.Message}");
            throw;
        }
    }

    [HttpPost("failure")]
    public async Task<IActionResult> Failure([FromBody] FailedSubscriptionPaymentRequest request)
    {
        try
        {
            var marked = await subscriptions.MarkPaymentFailedAsync(CurrentUserId.ToString(), request.PaymentId, request.OrderId);
            Logger.Info($"{nameof(Failure)} | RequestId: {HttpContext.TraceIdentifier} | UserId: {CurrentUserId} | " +
                $"Payload: {JsonSerializer.Serialize(request)} | Recorded: {marked}");
            return marked
                ? Ok(ApiResponse.SuccessResult("Failed checkout recorded; you can retry."))
                : Conflict(ApiResponse.ErrorResult("Payment is no longer pending."));
        }
        catch (Exception ex)
        {
            Logger.Error(ex, $"{nameof(Failure)} | RequestId: {HttpContext.TraceIdentifier} | UserId: {CurrentUserId} | " +
                $"Payload: {JsonSerializer.Serialize(request)} | Exception: {ex.Message}");
            throw;
        }
    }

    [AllowAnonymous]
    [HttpPost("webhook/razorpay")]
    public async Task<IActionResult> RazorpayWebhook()
    {
        var payload = string.Empty;
        try
        {
            using var reader = new StreamReader(Request.Body);
            payload = await reader.ReadToEndAsync();
            var eventId = Request.Headers["X-Razorpay-Event-Id"].ToString();
            var signature = Request.Headers["X-Razorpay-Signature"].ToString();
            if (string.IsNullOrWhiteSpace(eventId))
            {
                Logger.Warn($"{nameof(RazorpayWebhook)} | RequestId: {HttpContext.TraceIdentifier} | Payload: [redacted webhook body, length={payload.Length}] | Missing event ID");
                return BadRequest(new { error = "missing_event_id" });
            }
            var result = await subscriptions.ProcessWebhookAsync(eventId, payload, signature);
            Logger.Info($"{nameof(RazorpayWebhook)} | RequestId: {HttpContext.TraceIdentifier} | EventId: {eventId} | " +
                $"Payload: {SerializeSafeJson(payload)} | ValidSignature: {result.IsValidSignature} | Duplicate: {result.Duplicate}");
            if (!result.IsValidSignature) return Unauthorized(new { error = "invalid_signature" });
            return Ok(new { accepted = true, duplicate = result.Duplicate });
        }
        catch (Exception ex)
        {
            Logger.Error(ex, $"{nameof(RazorpayWebhook)} | RequestId: {HttpContext.TraceIdentifier} | " +
                $"Payload: {SerializeSafeJson(payload)} | Exception: {ex.Message}");
            throw;
        }
    }

    private static readonly string[] SensitivePayloadNames =
        ["password", "token", "secret", "signature", "apikey", "authorization", "credential", "idempotencykey"];

    private static string SerializeSafePayload(object payload)
    {
        var node = JsonSerializer.SerializeToNode(payload);
        RedactSensitiveFields(node);
        return node?.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) ?? "null";
    }

    private static string SerializeSafeJson(string payload)
    {
        try
        {
            var node = JsonNode.Parse(payload);
            RedactSensitiveFields(node);
            return node?.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) ?? "null";
        }
        catch (JsonException)
        {
            return $"[invalid JSON payload, length={payload.Length}]";
        }
    }

    private static void RedactSensitiveFields(JsonNode? node)
    {
        if (node is JsonObject jsonObject)
        {
            foreach (var property in jsonObject.ToList())
            {
                if (SensitivePayloadNames.Any(name => property.Key.Contains(name, StringComparison.OrdinalIgnoreCase)))
                    jsonObject[property.Key] = "[REDACTED]";
                else
                    RedactSensitiveFields(property.Value);
            }
        }
        else if (node is JsonArray jsonArray)
        {
            foreach (var item in jsonArray)
                RedactSensitiveFields(item);
        }
    }
}

public sealed record StartSubscriptionRequest(string BillingPeriod, bool StartTrial, string IdempotencyKey);
public sealed record VerifySubscriptionPaymentRequest(Guid PaymentId, string OrderId, string RazorpayPaymentId, string Signature);
public sealed record FailedSubscriptionPaymentRequest(Guid PaymentId, string OrderId);
