using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using RJ.Api.Operations;

namespace RJ.ApiTests;

public sealed class ApiExceptionBoundaryMiddlewareTests
{
    [Fact]
    public async Task Unexpected_api_exception_returns_stable_sanitized_500()
    {
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/cases/case-1/documents";
        context.Response.Body = new MemoryStream();
        context.Items[CorrelationIdMiddleware.ItemName] = "corr-1";
        var middleware = new ApiExceptionBoundaryMiddleware(
            _ => throw new InvalidOperationException("secret internal detail"),
            NullLogger<ApiExceptionBoundaryMiddleware>.Instance);

        await middleware.InvokeAsync(context);

        Assert.Equal(StatusCodes.Status500InternalServerError, context.Response.StatusCode);
        context.Response.Body.Position = 0;
        using var document = await System.Text.Json.JsonDocument.ParseAsync(context.Response.Body);
        Assert.Equal("internal_error", document.RootElement.GetProperty("code").GetString());
        Assert.Equal("An internal error occurred.", document.RootElement.GetProperty("error").GetString());
        Assert.DoesNotContain("secret internal detail", document.RootElement.GetRawText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Non_api_exception_is_not_converted_by_the_api_boundary()
    {
        var context = new DefaultHttpContext();
        context.Request.Path = "/health/live";
        var middleware = new ApiExceptionBoundaryMiddleware(
            _ => throw new InvalidOperationException("health failure"),
            NullLogger<ApiExceptionBoundaryMiddleware>.Instance);

        await Assert.ThrowsAsync<InvalidOperationException>(() => middleware.InvokeAsync(context));
    }
}
