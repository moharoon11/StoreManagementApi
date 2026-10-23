using System.Security.Claims;
using StoreManagement.Api.Services;

namespace StoreManagement.Api.Middleware;

public sealed class SubscriptionAccessMiddleware(RequestDelegate next, ILogger<SubscriptionAccessMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context, SubscriptionPlatformClient platform)
    {
        var path = context.Request.Path;
        if (!path.StartsWithSegments("/api") || path.StartsWithSegments("/api/auth") ||
            path.StartsWithSegments("/api/subscription") || path.StartsWithSegments("/api/store/profile") ||
            path.StartsWithSegments("/health") || context.User.Identity?.IsAuthenticated != true)
        {
            await next(context);
            return;
        }

        var userId = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? context.User.FindFirst("userId")?.Value;
        if (!int.TryParse(userId, out var id)) { context.Response.StatusCode = StatusCodes.Status401Unauthorized; return; }
        try
        {
            using var status = await platform.GetStatusAsync(id, context.RequestAborted);
            var hasAccess = status.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object &&
                status.RootElement.TryGetProperty("has_access", out var snake) && snake.ValueKind == System.Text.Json.JsonValueKind.Number && snake.GetInt32() == 1;
            if (!hasAccess)
            {
                context.Response.StatusCode = StatusCodes.Status402PaymentRequired;
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsJsonAsync(new { success = false, message = "An active subscription or free trial is required.", code = "subscription_required" }, context.RequestAborted);
                return;
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Subscription access check failed for authenticated user {UserId}", id);
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            await context.Response.WriteAsJsonAsync(new { success = false, message = "Subscription service is temporarily unavailable." }, context.RequestAborted);
            return;
        }
        await next(context);
    }
}
