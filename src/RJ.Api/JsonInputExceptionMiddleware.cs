using Microsoft.AspNetCore.Http;

namespace RJ.Api;

public sealed class JsonInputExceptionMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (BadHttpRequestException exception) when (
            context.Request.Path.Equals("/api/legal-documents", StringComparison.Ordinal)
            && exception.StatusCode == StatusCodes.Status400BadRequest)
        {
            if (context.Response.HasStarted)
            {
                throw;
            }

            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsJsonAsync(
                new ApiError("invalid_json", "Request body is not valid for the ingestion contract."),
                context.RequestAborted);
        }
    }
}
