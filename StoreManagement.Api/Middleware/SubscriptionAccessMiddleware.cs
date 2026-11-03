using System.Security.Claims;
using System.Text.Json;
using NLog;
using SubscriptionPlatform.Core;

namespace StoreManagement.Api.Middleware;

public sealed class SubscriptionAccessMiddleware(RequestDelegate next)
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
    public async Task InvokeAsync(HttpContext context, StoreSubscriptionService subscriptions)
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
        if (!int.TryParse(userId, out var id))
        {
            Logger.Warn(this.MethodName() + " | Subscription check rejected: authenticated request has no valid user ID. RequestId: {0}", context.TraceIdentifier);
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        try
        {
            var status = await subscriptions.GetStatusAsync(id.ToString());
            var json = JsonSerializer.SerializeToElement(status);
            var hasAccess = json.ValueKind == JsonValueKind.Object &&
                json.TryGetProperty("has_access", out var access) &&
                (access.ValueKind == JsonValueKind.True || access.ValueKind == JsonValueKind.Number && access.GetInt32() == 1);
            if (!hasAccess)
            {
                Logger.Warn(this.MethodName() + " | Subscription access denied. UserId: {0}, RequestId: {1}, Path: {2}", id, context.TraceIdentifier, path);
                context.Response.StatusCode = StatusCodes.Status402PaymentRequired;
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsJsonAsync(new { success = false, message = "An active subscription or free trial is required.", code = "subscription_required" }, context.RequestAborted);
                return;
            }
        }
        catch (Exception ex)
        {
            Logger.Error(ex, this.MethodName() + " | Subscription access check failed for authenticated user {0}", id);
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            await context.Response.WriteAsJsonAsync(new { success = false, message = "Subscription service is temporarily unavailable." }, context.RequestAborted);
            return;
        }
        await next(context);
    }
}
