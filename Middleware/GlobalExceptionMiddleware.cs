using System.Net;
using System.Text.Json;
using StoreManagement.Api.Helpers;

namespace StoreManagement.Api.Middleware
{
    public class GlobalExceptionMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<GlobalExceptionMiddleware> _logger;

        public GlobalExceptionMiddleware(RequestDelegate next, ILogger<GlobalExceptionMiddleware> logger)
        {
            _next = next;
            _logger = logger;
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
                await HandleExceptionAsync(context, ex);
            }
        }

        private static Task HandleExceptionAsync(HttpContext context, Exception exception)
        {
            context.Response.ContentType = "application/json";

            var response = exception switch
            {
                KeyNotFoundException => (HttpStatusCode.NotFound, ApiResponse.ErrorResult(exception.Message)),
                UnauthorizedAccessException => (HttpStatusCode.Unauthorized, ApiResponse.ErrorResult(exception.Message)),
                InvalidOperationException => (HttpStatusCode.BadRequest, ApiResponse.ErrorResult(exception.Message)),
                ArgumentException => (HttpStatusCode.BadRequest, ApiResponse.ErrorResult(exception.Message)),
                _ => (HttpStatusCode.InternalServerError, ApiResponse.ErrorResult($"An internal server error occurred: {exception.Message}"))
            };

            context.Response.StatusCode = (int)response.Item1;

            var jsonOptions = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };

            return context.Response.WriteAsync(JsonSerializer.Serialize(response.Item2, jsonOptions));
        }
    }
}
