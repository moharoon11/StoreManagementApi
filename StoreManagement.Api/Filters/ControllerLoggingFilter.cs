using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using NLog;

namespace StoreManagement.Api.Filters;

public sealed class ControllerLoggingFilter : IAsyncActionFilter, IAsyncResultFilter
{
    internal const string FailureLoggedItemKey = "ControllerLoggingFailureLogged";
    private const string StartedAtItemKey = "ControllerLoggingStartedAt";
    private const string PayloadItemKey = "ControllerLoggingPayload";
    private static readonly string[] SensitiveNames = ["password", "token", "secret", "signature", "apikey", "authorization", "credential", "idempotencykey"];
    private static readonly JsonSerializerOptions ReadableJson = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        if (context.ActionDescriptor is not ControllerActionDescriptor action)
        {
            await next();
            return;
        }

        var startedAt = DateTimeOffset.UtcNow;
        context.HttpContext.Items[StartedAtItemKey] = startedAt;
        var payload = BuildPayload(context.ActionArguments);
        context.HttpContext.Items[PayloadItemKey] = payload;
        using var requestScope = ScopeContext.PushProperty("RequestId", context.HttpContext.TraceIdentifier);
        var userId = context.HttpContext.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
            ?? context.HttpContext.User.FindFirst("userId")?.Value;
        using var userScope = userId is null ? null : ScopeContext.PushProperty("UserId", userId);
        var logger = LogManager.GetLogger(action.ControllerTypeInfo.Name);

        logger.Debug(ToReadableJson(new
        {
            timestampUtc = startedAt,
            level = "DEBUG",
            eventType = "method_entered",
            requestId = context.HttpContext.TraceIdentifier,
            controller = action.ControllerName,
            methodName = action.ActionName,
            httpMethod = context.HttpContext.Request.Method,
            path = context.HttpContext.Request.Path.Value,
            payload,
            reason = "Controller action started."
        }));

        var executed = await next();
        if (executed.Exception is not null && !executed.ExceptionHandled)
        {
            context.HttpContext.Items[FailureLoggedItemKey] = true;
            WriteFailure(context.HttpContext, action, executed.Exception, 500);
        }
    }

    public async Task OnResultExecutionAsync(ResultExecutingContext context, ResultExecutionDelegate next)
    {
        if (context.ActionDescriptor is not ControllerActionDescriptor action)
        {
            await next();
            return;
        }

        var executed = await next();
        var statusCode = context.HttpContext.Response.StatusCode;
        var startedAt = context.HttpContext.Items[StartedAtItemKey] as DateTimeOffset?;
        var durationMs = startedAt.HasValue
            ? Math.Round((DateTimeOffset.UtcNow - startedAt.Value).TotalMilliseconds, 2)
            : (double?)null;

        var succeeded = statusCode < StatusCodes.Status400BadRequest;
        var logData = new
        {
            timestampUtc = DateTimeOffset.UtcNow,
            level = succeeded ? "INFO" : statusCode < 500 ? "WARN" : "ERROR",
            eventType = "method_finished",
            requestId = context.HttpContext.TraceIdentifier,
            controller = action.ControllerName,
            methodName = action.ActionName,
            responseStatus = statusCode,
            outcome = succeeded ? "succeeded" : "failed",
            durationMs,
            payload = context.HttpContext.Items[PayloadItemKey],
            reason = succeeded
                ? "Action completed with a successful HTTP response."
                : $"Action completed with HTTP {statusCode}."
        };

        var logger = LogManager.GetLogger(action.ControllerTypeInfo.Name);
        if (succeeded)
        {
            logger.Info(ToReadableJson(logData));
        }
        else if (statusCode < 500)
        {
            logger.Warn(ToReadableJson(logData));
        }
        else
        {
            logger.Error(ToReadableJson(logData));
        }
    }

    public static void WriteFailure(HttpContext context, ControllerActionDescriptor action, Exception exception, int statusCode)
    {
        var message = ToReadableJson(new
        {
            timestampUtc = DateTimeOffset.UtcNow,
            level = "ERROR",
            eventType = "method_failed",
            requestId = context.TraceIdentifier,
            controller = action.ControllerName,
            methodName = action.ActionName,
            responseStatus = statusCode,
            outcome = "failed",
            reason = exception.Message,
            exceptionType = exception.GetType().Name,
            payload = context.Items[PayloadItemKey]
        });

        LogManager.GetLogger(action.ControllerTypeInfo.Name).Error(exception, message);
    }

    private static object BuildPayload(IDictionary<string, object?> actionArguments) =>
        actionArguments.ToDictionary(pair => pair.Key, pair => SanitizeValue(pair.Key, pair.Value));

    private static object? SanitizeValue(string name, object? value)
    {
        if (IsSensitive(name))
        {
            return "[REDACTED]";
        }

        if (value is IFormFile file)
        {
            return new { file.FileName, file.ContentType, file.Length };
        }

        if (value is null || value is string || value.GetType().IsPrimitive || value is decimal || value is DateTime || value is DateTimeOffset || value is Guid)
        {
            return value;
        }

        try
        {
            var node = JsonSerializer.SerializeToNode(value);
            RedactSensitiveFields(node);
            return node;
        }
        catch
        {
            return "[Payload could not be serialized]";
        }
    }

    private static void RedactSensitiveFields(JsonNode? node)
    {
        if (node is JsonObject jsonObject)
        {
            foreach (var property in jsonObject.ToList())
            {
                if (IsSensitive(property.Key))
                {
                    jsonObject[property.Key] = "[REDACTED]";
                }
                else
                {
                    RedactSensitiveFields(property.Value);
                }
            }
        }
        else if (node is JsonArray jsonArray)
        {
            foreach (var item in jsonArray)
            {
                RedactSensitiveFields(item);
            }
        }
    }

    private static bool IsSensitive(string name) =>
        SensitiveNames.Any(sensitive => name.Contains(sensitive, StringComparison.OrdinalIgnoreCase));

    private static string ToReadableJson(object value) => JsonSerializer.Serialize(value, ReadableJson);
}
