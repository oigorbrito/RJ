using Microsoft.AspNetCore.Http;
using RJ.Api;
using RJ.Application.Operations;

namespace RJ.ApiTests;

public sealed class HealthEndpointTests
{
    [Fact]
    public void Live_returns_200_without_dependency_probe()
    {
        var result = HealthEndpoint.Live();

        Assert.Equal(StatusCodes.Status200OK, ((IStatusCodeHttpResult)result).StatusCode);
        var payload = Assert.IsType<HealthStatus>(((IValueHttpResult)result).Value);
        Assert.Equal("live", payload.Status);
    }

    [Fact]
    public async Task ReadyAsync_returns_200_when_probe_passes()
    {
        var result = await HealthEndpoint.ReadyAsync(new StubProbe(), CancellationToken.None);

        Assert.Equal(StatusCodes.Status200OK, ((IStatusCodeHttpResult)result).StatusCode);
        var payload = Assert.IsType<HealthStatus>(((IValueHttpResult)result).Value);
        Assert.Equal("ready", payload.Status);
    }

    [Fact]
    public async Task ReadyAsync_returns_503_without_exposing_dependency_error()
    {
        var result = await HealthEndpoint.ReadyAsync(
            new StubProbe(new InvalidOperationException("connection string secret")),
            CancellationToken.None);

        Assert.Equal(StatusCodes.Status503ServiceUnavailable, ((IStatusCodeHttpResult)result).StatusCode);
        var payload = Assert.IsType<HealthStatus>(((IValueHttpResult)result).Value);
        Assert.Equal("not_ready", payload.Status);
        Assert.DoesNotContain("secret", payload.Status, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadyAsync_maps_internal_cancellation_to_503()
    {
        var result = await HealthEndpoint.ReadyAsync(
            new StubProbe(new OperationCanceledException()),
            CancellationToken.None);

        Assert.Equal(StatusCodes.Status503ServiceUnavailable, ((IStatusCodeHttpResult)result).StatusCode);
    }

    [Fact]
    public async Task ReadyAsync_propagates_request_cancellation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            HealthEndpoint.ReadyAsync(new CancellationAwareProbe(), cancellation.Token));
    }

    private sealed class StubProbe(Exception? exception = null) : IReadinessProbe
    {
        public Task CheckAsync(CancellationToken cancellationToken)
        {
            if (exception is not null)
            {
                return Task.FromException(exception);
            }

            return Task.CompletedTask;
        }
    }

    private sealed class CancellationAwareProbe : IReadinessProbe
    {
        public Task CheckAsync(CancellationToken cancellationToken) =>
            Task.FromCanceled(cancellationToken);
    }
}
