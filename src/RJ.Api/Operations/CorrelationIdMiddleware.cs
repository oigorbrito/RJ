namespace RJ.Api.Operations;

public sealed class CorrelationIdMiddleware(RequestDelegate next)
{
    public const string HeaderName = "X-Correlation-Id";
    public const string ItemName = "RJ.CorrelationId";

    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var correlationId = context.Request.Headers[HeaderName].FirstOrDefault();
        if (!Guid.TryParse(correlationId, out var parsed))
        {
            parsed = Guid.NewGuid();
            correlationId = parsed.ToString("D");
        }
        else
        {
            correlationId = parsed.ToString("D");
        }

        context.Items[ItemName] = correlationId;
        context.Response.Headers[HeaderName] = correlationId;
        await next(context);
    }
}
