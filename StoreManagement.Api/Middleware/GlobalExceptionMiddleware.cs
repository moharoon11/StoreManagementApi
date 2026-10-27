using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Controllers;
using NLog;
using StoreManagement.Api.Filters;
using StoreManagement.Api.Helpers;

namespace StoreManagement.Api.Middleware;

/// <summary>
/// Keeps the API error response contract stable for the frontend and records
/// exceptions that escape controller-level handling.
/// </summary>
public sealed class GlobalExceptionMiddleware(RequestDelegate next)
{
    private static readonly Logger Logger = LogManager.GetLogger("GlobalExceptionMiddleware");

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (Exception ex)
        {
            var response = CreateErrorResponse(ex);
            var action = context.GetEndpoint()?.Metadata.GetMetadata<ControllerActionDescriptor>();
            var alreadyLoggedByFilter = context.Items[ControllerLoggingFilter.FailureLoggedItemKey] is true;
            var message = $"{nameof(InvokeAsync)} | RequestId: {context.TraceIdentifier} | " +
                $"Path: {context.Request.Path} | StatusCode: {(int)response.StatusCode} | Message: {ex.Message}";

            // Keep subscription failures in the subscription file series only.
            if (action?.ControllerTypeInfo.Name.Contains("Subscription", StringComparison.OrdinalIgnoreCase) == true)
            {
                if (!alreadyLoggedByFilter)
                    LogManager.GetLogger(action.ControllerTypeInfo.Name).Error(ex,
                        $"{action.ActionName} | {message} | Payload: {JsonSerializer.Serialize(context.Request.RouteValues)}");
            }
            else
            {
                Logger.Error(ex, message);
                if (!alreadyLoggedByFilter && action is not null)
                    LogManager.GetLogger(action.ControllerTypeInfo.Name).Error(ex,
                        $"{action.ActionName} | RequestId: {context.TraceIdentifier} | " +
                        $"Payload: {JsonSerializer.Serialize(context.Request.RouteValues)} | Exception: {ex.Message}");
            }

            if (context.Response.HasStarted)
            {
                Logger.Error($"{nameof(InvokeAsync)} | RequestId: {context.TraceIdentifier} | " +
                    "Response already started; the API error body cannot be replaced.");
                return;
            }

            context.Response.Clear();
            await HandleExceptionAsync(context, response);
        }
    }

    private static (HttpStatusCode StatusCode, ApiResponse Response) CreateErrorResponse(Exception exception) =>
        exception switch
        {
            KeyNotFoundException => (HttpStatusCode.NotFound, ApiResponse.ErrorResult(exception.Message)),
            UnauthorizedAccessException => (HttpStatusCode.Unauthorized, ApiResponse.ErrorResult(exception.Message)),
            InvalidOperationException => (HttpStatusCode.BadRequest, ApiResponse.ErrorResult(exception.Message)),
            ArgumentException => (HttpStatusCode.BadRequest, ApiResponse.ErrorResult(exception.Message)),
            BadHttpRequestException => (HttpStatusCode.BadRequest, ApiResponse.ErrorResult(exception.Message)),
            _ => (HttpStatusCode.InternalServerError, ApiResponse.ErrorResult("An unexpected server error occurred."))
        };

    private static Task HandleExceptionAsync(HttpContext context, (HttpStatusCode StatusCode, ApiResponse Response) response)
    {
        context.Response.ContentType = "application/json";
        context.Response.StatusCode = (int)response.StatusCode;
        return context.Response.WriteAsync(JsonSerializer.Serialize(response.Response, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        }));
    }
}
