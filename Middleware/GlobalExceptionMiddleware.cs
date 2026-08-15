using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Controllers;
using StoreManagement.Api.Filters;
using StoreManagement.Api.Helpers;
using StoreManagement.Api.Logging;

namespace StoreManagement.Api.Middleware
{
    public class GlobalExceptionMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<GlobalExceptionMiddleware> _logger;
        private readonly IControllerFileLogger _controllerFileLogger;

        public GlobalExceptionMiddleware(RequestDelegate next, ILogger<GlobalExceptionMiddleware> logger, IControllerFileLogger controllerFileLogger)
        {
            _next = next;
            _logger = logger;
            _controllerFileLogger = controllerFileLogger;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            try
            {
                await _next(context);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An unhandled exception occurred: {Message}", ex.Message);
                var response = CreateErrorResponse(ex);

                if (context.Items[ControllerLoggingFilter.FailureLoggedItemKey] is not true &&
                    context.GetEndpoint()?.Metadata.GetMetadata<ControllerActionDescriptor>() is { } action)
                {
                    await _controllerFileLogger.WriteAsync(action.ControllerTypeInfo.Name, new
                    {
                        timestampUtc = DateTimeOffset.UtcNow,
                        eventType = "method_failed",
                        requestId = context.TraceIdentifier,
                        controller = action.ControllerName,
                        methodName = action.ActionName,
                        responseStatus = (int)response.StatusCode,
                        outcome = "failed",
                        reason = ex.Message,
                        exceptionType = ex.GetType().Name
                    }, context.RequestAborted);
                }

                await HandleExceptionAsync(context, response);
            }
        }

        private static (HttpStatusCode StatusCode, ApiResponse Response) CreateErrorResponse(Exception exception)
        {
            return exception switch
            {
                KeyNotFoundException => (HttpStatusCode.NotFound, ApiResponse.ErrorResult(exception.Message)),
                UnauthorizedAccessException => (HttpStatusCode.Unauthorized, ApiResponse.ErrorResult(exception.Message)),
                InvalidOperationException => (HttpStatusCode.BadRequest, ApiResponse.ErrorResult(exception.Message)),
                ArgumentException => (HttpStatusCode.BadRequest, ApiResponse.ErrorResult(exception.Message)),
                BadHttpRequestException => (HttpStatusCode.BadRequest, ApiResponse.ErrorResult(exception.Message)),
                _ => (HttpStatusCode.InternalServerError, ApiResponse.ErrorResult("An unexpected server error occurred."))
            };
        }

        private static Task HandleExceptionAsync(HttpContext context, (HttpStatusCode StatusCode, ApiResponse Response) response)
        {
            context.Response.ContentType = "application/json";
            context.Response.StatusCode = (int)response.StatusCode;

            var jsonOptions = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };

            return context.Response.WriteAsync(JsonSerializer.Serialize(response.Response, jsonOptions));
        }
    }
}
