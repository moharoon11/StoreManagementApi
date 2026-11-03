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
        // This endpoint gives the app the plan name, trial period, currency, and prices
        // that it should show before the customer starts a subscription.
        try
        {
            // Load the configured subscription plan and its available terms.
            var plan = await subscriptions.GetOptionsAsync();
            if (plan is null)
                Logger.Warn(this.MethodName() + " | Configured subscription plan was not found. RequestId: {0}", HttpContext.TraceIdentifier);
            else
                Logger.Info(this.MethodName() + " | Subscription options loaded. RequestId: {0}, UserId: {1}", HttpContext.TraceIdentifier, CurrentUserId);
            return plan is null
                ? StatusCode(503, ApiResponse.ErrorResult("Subscription plan is not configured."))
                : Ok(ApiResponse<object>.SuccessResult(plan, "Subscription options loaded."));
        }
        catch (Exception ex)
        {
            Logger.Error(ex, this.MethodName() + " | Options failed. RequestId: {0}", HttpContext.TraceIdentifier);
            return StatusCode(503, ApiResponse.ErrorResult("Subscription service is temporarily unavailable."));
        }
    }

    [HttpGet("status")]
    public async Task<IActionResult> Status()
    {
        // This endpoint tells the app whether the signed-in user currently has subscription access.
        try
        {
            // Look up the current user's latest subscription using the authenticated user ID.
            var status = await subscriptions.GetStatusAsync(CurrentUserId.ToString());
            Logger.Info($"{this.MethodName()} | RequestId: {HttpContext.TraceIdentifier} | UserId: {CurrentUserId} | Payload: none");
            // Return a safe "no subscription" status when no subscription record exists.
            return Ok(ApiResponse<object>.SuccessResult(status ?? new { has_access = false, status = "none" }, "Subscription status loaded."));
        }
        catch (Exception ex)
        {
            Logger.Error(ex, this.MethodName() + " | Status failed. RequestId: {0}", HttpContext.TraceIdentifier);
            return StatusCode(503, ApiResponse.ErrorResult("Subscription service is temporarily unavailable."));
        }
    }

    [HttpPost("start")]
    public async Task<IActionResult> Start([FromBody] StartSubscriptionRequest request)
    {
        // This endpoint starts a trial or creates a server-priced Razorpay checkout order.
        // The client supplies the term and retry key; the server chooses the customer and price.
        try
        {
            if (!request.StartTrial && request.PlanOptionId is null && string.IsNullOrWhiteSpace(request.BillingPeriod))
            {
                return BadRequest(ApiResponse.ErrorResult("Choose a subscription option."));
            }
            if (string.IsNullOrWhiteSpace(request.IdempotencyKey) || request.IdempotencyKey.Length > 80)
            {
                // The same key lets a retry return the original subscription/order instead of creating another one.
                Logger.Warn($"{this.MethodName()} | RequestId: {HttpContext.TraceIdentifier} | UserId: {CurrentUserId} | " +
                    $"Payload: {SerializeSafePayload(new { request.BillingPeriod, request.StartTrial, request.IdempotencyKey })} | Invalid idempotency key");
                return BadRequest(ApiResponse.ErrorResult("A valid idempotency key is required."));
            }
            try
            {
                // Start a trial or create a paid checkout order for the authenticated user.
                var result = await subscriptions.StartAsync(CurrentUserId.ToString(), request.BillingPeriod, request.StartTrial, request.IdempotencyKey, request.PlanOptionId);
                Logger.Info($"{this.MethodName()} | RequestId: {HttpContext.TraceIdentifier} | UserId: {CurrentUserId} | " +
                    $"Payload: {SerializeSafePayload(request)} | SubscriptionId: {result.SubscriptionId} | Status: {result.Status}");
                return Ok(ApiResponse<StoreSubscriptionOrder>.SuccessResult(result, "Checkout initialized."));
            }
            catch (ArgumentException ex)
            {
                Logger.Warn(this.MethodName() + " | Start rejected. RequestId: {0}, Reason: {1}", HttpContext.TraceIdentifier, ex.Message);
                return BadRequest(ApiResponse.ErrorResult(ex.Message));
            }
            catch (InvalidOperationException ex)
            {
                Logger.Warn(this.MethodName() + " | Start conflicted. RequestId: {0}, Reason: {1}", HttpContext.TraceIdentifier, ex.Message);
                return Conflict(ApiResponse.ErrorResult(ex.Message));
            }
            catch (Exception ex)
            {
                Logger.Error(ex, this.MethodName() + " | Start failed. RequestId: {0}", HttpContext.TraceIdentifier);
                return StatusCode(500, ApiResponse.ErrorResult("Unable to initialize checkout."));
            }
        }
        catch (Exception ex)
        {
            return ApiExceptionHandler.Handle(this, ex, Logger, nameof(Start));
        }
    }

    [HttpPost("verify")]
    public async Task<IActionResult> Verify([FromBody] VerifySubscriptionPaymentRequest request)
    {
        // This endpoint is called after checkout; access is activated only after the server verifies Razorpay.
        try
        {
            // Verify the Razorpay signature and provider payment details before activating access.
            var verified = await subscriptions.VerifyPaymentAsync(request.PaymentId, request.OrderId, request.RazorpayPaymentId, request.Signature);
            if (verified)
                Logger.Info(this.MethodName() + " | Payment verified. RequestId: {0}, PaymentId: {1}, OrderId: {2}", HttpContext.TraceIdentifier, request.PaymentId, request.OrderId);
            else
                Logger.Warn(this.MethodName() + " | Payment verification rejected. RequestId: {0}, PaymentId: {1}, OrderId: {2}", HttpContext.TraceIdentifier, request.PaymentId, request.OrderId);
            return verified
                ? Ok(ApiResponse.SuccessResult("Payment captured and verified."))
                : BadRequest(ApiResponse.ErrorResult("Razorpay payment could not be verified. Access was not activated."));
        }
        catch (Exception ex)
        {
            return ApiExceptionHandler.Handle(this, ex, Logger, nameof(Verify));
        }
    }

    [HttpPost("failure")]
    public async Task<IActionResult> Failure([FromBody] FailedSubscriptionPaymentRequest request)
    {
        // This endpoint records a checkout failure so the pending subscription cannot remain open forever.
        try
        {
            // Mark the matching pending checkout as failed for the authenticated user.
            var marked = await subscriptions.MarkPaymentFailedAsync(CurrentUserId.ToString(), request.PaymentId, request.OrderId);
            if (marked)
                Logger.Info(this.MethodName() + " | Failed checkout recorded. RequestId: {0}, PaymentId: {1}, OrderId: {2}", HttpContext.TraceIdentifier, request.PaymentId, request.OrderId);
            else
                Logger.Warn(this.MethodName() + " | Failed checkout callback rejected: payment no longer pending. RequestId: {0}, PaymentId: {1}", HttpContext.TraceIdentifier, request.PaymentId);
            return marked
                ? Ok(ApiResponse.SuccessResult("Failed checkout recorded; you can retry."))
                : Conflict(ApiResponse.ErrorResult("Payment is no longer pending."));
        }
        catch (Exception ex)
        {
            return ApiExceptionHandler.Handle(this, ex, Logger, nameof(Failure));
        }
    }

    [AllowAnonymous]
    [HttpPost("webhook/razorpay")]
    public async Task<IActionResult> RazorpayWebhook()
    {
        // Razorpay calls this endpoint independently of the app so delayed or missed checkout callbacks
        // can still update the payment and subscription in the database.
        var payload = string.Empty;
        try
        {
            // Read the raw provider payload because its exact bytes are needed for signature verification.
            using var reader = new StreamReader(Request.Body);
            payload = await reader.ReadToEndAsync();
            // Read the provider event ID and signature from the request headers.
            var eventId = Request.Headers["X-Razorpay-Event-Id"].ToString();
            var signature = Request.Headers["X-Razorpay-Signature"].ToString();
            if (string.IsNullOrWhiteSpace(eventId))
            {
                Logger.Warn($"{this.MethodName()} | RequestId: {HttpContext.TraceIdentifier} | Payload: [redacted webhook body, length={payload.Length}] | Missing event ID");
                return BadRequest(new { error = "missing_event_id" });
            }
            // Verify and reconcile the provider event, including duplicate-event handling.
            var result = await subscriptions.ProcessWebhookAsync(eventId, payload, signature);
            if (!result.IsValidSignature)
            {
                Logger.Warn(this.MethodName() + " | Razorpay webhook rejected: invalid signature or payment mismatch. EventId: {0}, PayloadLength: {1}", eventId, payload.Length);
                return Unauthorized(new { error = "invalid_signature" });
            }
            Logger.Info($"{this.MethodName()} | RequestId: {HttpContext.TraceIdentifier} | EventId: {eventId} | " +
                $"Payload: {SerializeSafeJson(payload)} | Duplicate: {result.Duplicate}");
            return Ok(new { accepted = true, duplicate = result.Duplicate });
        }
        catch (Exception ex)
        {
            return ApiExceptionHandler.Handle(this, ex, Logger, nameof(RazorpayWebhook));
        }
    }

    private static readonly string[] SensitivePayloadNames =
        ["password", "token", "secret", "signature", "apikey", "authorization", "credential", "idempotencykey"];

    private static string SerializeSafePayload(object payload)
    {
        // Convert a request object to JSON so sensitive fields can be removed before logging.
        var node = JsonSerializer.SerializeToNode(payload);
        RedactSensitiveFields(node);
        return node?.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) ?? "null";
    }

    private static string SerializeSafeJson(string payload)
    {
        try
        {
            // Parse and sanitize valid JSON webhook content before it is written to logs.
            var node = JsonNode.Parse(payload);
            RedactSensitiveFields(node);
            return node?.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) ?? "null";
        }
        catch (JsonException ex)
        {
            // Avoid logging malformed content directly; expose only its length.
            Logger.Error(ex, "SerializeSafeJson | Failed to parse webhook JSON for logging. PayloadLength: {0}", payload.Length);
            return $"[invalid JSON payload, length={payload.Length}]";
        }
    }

    private static void RedactSensitiveFields(JsonNode? node)
    {
        if (node is JsonObject jsonObject)
        {
            // Walk through object properties and redact values whose names may contain secrets.
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
            // Apply the same redaction recursively to every item in an array.
            foreach (var item in jsonArray)
                RedactSensitiveFields(item);
        }
    }
}

public sealed record StartSubscriptionRequest(string? BillingPeriod, bool StartTrial, string IdempotencyKey, int? PlanOptionId = null);
public sealed record VerifySubscriptionPaymentRequest(Guid PaymentId, string OrderId, string RazorpayPaymentId, string Signature);
public sealed record FailedSubscriptionPaymentRequest(Guid PaymentId, string OrderId);
