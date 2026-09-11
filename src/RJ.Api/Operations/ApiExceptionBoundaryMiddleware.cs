using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace RJ.Api.Operations;

public sealed partial class ApiExceptionBoundaryMiddleware(
    RequestDelegate next,
    ILogger<ApiExceptionBoundaryMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (Exception exception) when (context.Request.Path.StartsWithSegments("/api"))
        {
            if (context.Response.HasStarted)
            {
                throw;
            }

            var correlationId = context.Items[CorrelationIdMiddleware.ItemName]?.ToString();
            LogUnhandledApiException(
                exception.GetType().Name,
                correlationId ?? "missing");

            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsJsonAsync(
                new ApiError("internal_error", "An internal error occurred."),
                context.RequestAborted);
        }
    }

    [LoggerMessage(
        EventId = 5001,
        Level = LogLevel.Error,
        Message = "Unhandled API exception type {ExceptionType} for correlation {CorrelationId}.")]
    private partial void LogUnhandledApiException(string exceptionType, string correlationId);
}
