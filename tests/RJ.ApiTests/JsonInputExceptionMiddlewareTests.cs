using System.Text.Json;
using Microsoft.AspNetCore.Http;
using RJ.Api;

namespace RJ.ApiTests;

public sealed class JsonInputExceptionMiddlewareTests
{
    [Fact]
    public async Task Middleware_maps_ingestion_bad_request_to_stable_400()
    {
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/legal-documents";
        context.Response.Body = new MemoryStream();
        var middleware = new JsonInputExceptionMiddleware(_ =>
            throw new BadHttpRequestException("sensitive parser detail", StatusCodes.Status400BadRequest));

        await middleware.InvokeAsync(context);

        context.Response.Body.Position = 0;
        using var json = await JsonDocument.ParseAsync(context.Response.Body);
        Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
        Assert.Equal("invalid_json", json.RootElement.GetProperty("code").GetString());
        Assert.Equal("Request body is not valid for the ingestion contract.", json.RootElement.GetProperty("error").GetString());
        Assert.DoesNotContain("sensitive parser detail", json.RootElement.GetRawText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Middleware_does_not_mask_bad_request_on_other_route()
    {
        var context = new DefaultHttpContext();
        context.Request.Path = "/other";
        var expected = new BadHttpRequestException("other", StatusCodes.Status400BadRequest);
        var middleware = new JsonInputExceptionMiddleware(_ => throw expected);

        var actual = await Assert.ThrowsAsync<BadHttpRequestException>(() => middleware.InvokeAsync(context));

        Assert.Same(expected, actual);
    }

    [Fact]
    public async Task Middleware_does_not_mask_unexpected_ingestion_failure()
    {
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/legal-documents";
        var expected = new InvalidOperationException("unexpected");
        var middleware = new JsonInputExceptionMiddleware(_ => throw expected);

        var actual = await Assert.ThrowsAsync<InvalidOperationException>(() => middleware.InvokeAsync(context));

        Assert.Same(expected, actual);
    }
}
