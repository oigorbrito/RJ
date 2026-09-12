using System.Security.Claims;

namespace RJ.Api;

public sealed class DemoAuthenticationMiddleware(RequestDelegate next)
{
    private static readonly string[] DemoCaseIds =
    [
        "demo-case-001",
        "demo-case-002",
        "demo-case-003",
        "demo-case-004",
        "demo-case-005"
    ];

    public async Task InvokeAsync(HttpContext context)
    {
        if (string.Equals(
                Environment.GetEnvironmentVariable("RJUDI_DEMO_MODE"),
                "true",
                StringComparison.OrdinalIgnoreCase))
        {
            var claims = new List<Claim>
            {
                new(ClaimsProcessSummaryCallerContextResolver.TenantIdClaim, "demo-tenant"),
                new(ClaimTypes.NameIdentifier, "demo-user"),
                new(ClaimsProcessSummaryCallerContextResolver.EvidenceSourceClaim, "demo-judit")
            };

            claims.AddRange(DemoCaseIds.Select(caseId =>
                new Claim(ClaimsProcessSummaryCallerContextResolver.CaseIdClaim, caseId)));

            context.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "RJudiDemo"));
        }

        await next(context);
    }
}
