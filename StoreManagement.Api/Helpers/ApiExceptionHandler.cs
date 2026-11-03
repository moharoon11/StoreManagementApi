using Microsoft.AspNetCore.Mvc;
using NLog;

namespace StoreManagement.Api.Helpers;

internal static class ApiExceptionHandler
{
    public static IActionResult Handle(ControllerBase controller, Exception exception, Logger logger, string action)
    {
        var (status, message) = exception switch
        {
            KeyNotFoundException => (StatusCodes.Status404NotFound, exception.Message),
            UnauthorizedAccessException => (StatusCodes.Status401Unauthorized, exception.Message),
            InvalidOperationException => (StatusCodes.Status400BadRequest, exception.Message),
            ArgumentException => (StatusCodes.Status400BadRequest, exception.Message),
            BadHttpRequestException => (StatusCodes.Status400BadRequest, exception.Message),
            _ => (StatusCodes.Status500InternalServerError, "An unexpected server error occurred.")
        };

        logger.Error(exception, "{0}.{1} failed. RequestId: {2}, Status: {3}",
            controller.GetType().Name, action, controller.HttpContext.TraceIdentifier, status);
        return new ObjectResult(ApiResponse.ErrorResult(message)) { StatusCode = status };
    }
}
