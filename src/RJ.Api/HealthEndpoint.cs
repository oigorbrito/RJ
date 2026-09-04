using Microsoft.AspNetCore.Http;
using RJ.Application.Operations;

namespace RJ.Api;

public static class HealthEndpoint
{
    public static readonly TimeSpan ReadinessTimeout = TimeSpan.FromSeconds(2);

    public static IResult Live() =>
        Results.Ok(new HealthStatus("live"));

    public static async Task<IResult> ReadyAsync(
        IReadinessProbe probe,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(probe);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(ReadinessTimeout);

        try
        {
            await probe.CheckAsync(timeout.Token);
            return Results.Ok(new HealthStatus("ready"));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return Results.Json(
                new HealthStatus("not_ready"),
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
        catch (Exception)
        {
            return Results.Json(
                new HealthStatus("not_ready"),
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    }
}

public sealed record HealthStatus(string Status);
