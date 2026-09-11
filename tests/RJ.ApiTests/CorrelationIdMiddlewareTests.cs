using Microsoft.AspNetCore.Http;
using RJ.Api.Operations;

namespace RJ.ApiTests;

public sealed class CorrelationIdMiddlewareTests
{
    [Fact]
    public async Task Valid_correlation_id_is_propagated_to_context_and_response()
    {
        var request = new DefaultHttpContext();
        request.Request.Headers[CorrelationIdMiddleware.HeaderName] = "8d1e6b40-1c0e-4a42-8f91-c4b5e4c8a0f2";
        var nextCalled = false;
        var middleware = new CorrelationIdMiddleware(context =>
        {
            nextCalled = true;
            Assert.Equal("8d1e6b40-1c0e-4a42-8f91-c4b5e4c8a0f2", context.Items[CorrelationIdMiddleware.ItemName]);
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(request);

        Assert.True(nextCalled);
        Assert.Equal("8d1e6b40-1c0e-4a42-8f91-c4b5e4c8a0f2", request.Response.Headers[CorrelationIdMiddleware.HeaderName].ToString());
    }

    [Fact]
    public async Task Missing_or_invalid_correlation_id_is_replaced_with_a_guid()
    {
        var request = new DefaultHttpContext();
        request.Request.Headers[CorrelationIdMiddleware.HeaderName] = "not-a-guid";
        var middleware = new CorrelationIdMiddleware(_ => Task.CompletedTask);

        await middleware.InvokeAsync(request);

        Assert.True(Guid.TryParse(request.Response.Headers[CorrelationIdMiddleware.HeaderName], out _));
    }
}
