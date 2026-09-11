using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using RJ.Api;

namespace RJ.HttpContractTests;

internal sealed class TestAuthenticatedPrincipalStartupFilter : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        app.Use(async (context, continuation) =>
        {
            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, "http-contract-subject"),
                new(ClaimsProcessSummaryCallerContextResolver.TenantIdClaim, "http-contract-tenant"),
                new(ClaimsProcessSummaryCallerContextResolver.EvidenceSourceClaim, "source.txt"),
                new(ClaimsProcessSummaryCallerContextResolver.EvidenceSourceClaim, "a.txt"),
                new(ClaimsProcessSummaryCallerContextResolver.EvidenceSourceClaim, "b.txt"),
                new(ClaimsProcessSummaryCallerContextResolver.EvidenceSourceClaim, "decisao.txt"),
                new(ClaimsProcessSummaryCallerContextResolver.EvidenceSourceClaim, "large.txt")
            };

            var caseId = ResolveCaseIdFromPath(context.Request.Path)
                ?? await ResolveCaseIdFromJsonBodyAsync(context.Request);
            if (!string.IsNullOrWhiteSpace(caseId))
            {
                claims.Add(new Claim(ClaimsProcessSummaryCallerContextResolver.CaseIdClaim, caseId));
            }

            context.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "http-contract-test"));
            await continuation();
        });

        next(app);
    };

    private static string? ResolveCaseIdFromPath(PathString path)
    {
        var segments = path.Value?
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return segments is { Length: >= 3 }
            && StringComparer.Ordinal.Equals(segments[0], "api")
            && StringComparer.Ordinal.Equals(segments[1], "cases")
            ? segments[2]
            : null;
    }

    private static async Task<string?> ResolveCaseIdFromJsonBodyAsync(HttpRequest request)
    {
        if (!HttpMethods.IsPost(request.Method)
            || !request.Path.Equals("/api/legal-documents", StringComparison.Ordinal)
            || request.ContentLength is null or 0)
        {
            return null;
        }

        request.EnableBuffering();
        try
        {
            using var document = await JsonDocument.ParseAsync(request.Body);
            if (document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("caseId", out var caseId)
                && caseId.ValueKind == JsonValueKind.String)
            {
                return caseId.GetString();
            }
        }
        catch (JsonException)
        {
            return null;
        }
        finally
        {
            request.Body.Position = 0;
        }

        return null;
    }
}
