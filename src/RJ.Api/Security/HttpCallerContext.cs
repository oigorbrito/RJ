using System.Security.Claims;
using RJ.Application.Security;

namespace RJ.Api.Security;

public interface IProcessCallerIdentity
{
    CallerContext Require(ClaimsPrincipal principal);

    CallerContext RequireCase(ClaimsPrincipal principal, string caseId);
}

public sealed class ClaimsPrincipalProcessCallerIdentity : IProcessCallerIdentity
{
    public CallerContext Require(ClaimsPrincipal principal) => HttpCallerContext.Require(principal);

    public CallerContext RequireCase(ClaimsPrincipal principal, string caseId) =>
        HttpCallerContext.RequireCase(principal, caseId);
}

public static class HttpCallerContext
{
    public const string TenantIdClaim = "tenant_id";
    public const string SubjectIdClaim = ClaimTypes.NameIdentifier;
    public const string TenantCaseAccessClaim = "tenant_case_access";
    public const string EvidenceSourceClaim = "evidence_source";
    public const string SealedCaseAccessClaim = "sealed_case_access";

    public static CallerContext Require(ClaimsPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);

        if (principal.Identity?.IsAuthenticated != true)
        {
            throw new UnauthorizedAccessException("An authenticated caller is required.");
        }

        var tenantId = RequiredClaim(principal, TenantIdClaim);
        var subjectId = RequiredClaim(principal, SubjectIdClaim);
        var caseIds = TenantScopedCaseIds(principal, tenantId);
        if (caseIds.Length == 0)
        {
            throw new UnauthorizedAccessException("The authenticated caller has no case authorization.");
        }

        var evidenceSources = Values(principal, EvidenceSourceClaim);
        return new CallerContext(
            tenantId,
            subjectId,
            caseIds,
            principal.HasClaim(SealedCaseAccessClaim, "true"),
            evidenceSources.Length == 0 ? null : evidenceSources);
    }

    public static CallerContext RequireCase(ClaimsPrincipal principal, string caseId)
    {
        var caller = Require(principal);
        if (!caller.IsAuthorizedForCase(caseId))
        {
            throw new ForbiddenAccessException("Caller is not authorized for this legal case.");
        }

        return caller;
    }

    private static string RequiredClaim(ClaimsPrincipal principal, string claimType) =>
        principal.FindFirst(claimType)?.Value?.Trim() is { Length: > 0 } value
            ? value
            : throw new UnauthorizedAccessException("The authenticated caller is missing a required identity claim.");

    private static string[] Values(ClaimsPrincipal principal, string claimType) =>
        principal.FindAll(claimType)
            .Select(claim => claim.Value.Trim())
            .Where(value => value.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

    private static string[] TenantScopedCaseIds(ClaimsPrincipal principal, string tenantId) =>
        Values(principal, TenantCaseAccessClaim)
            .Select(value => value.Split(':', 2))
            .Where(parts => parts.Length == 2 && StringComparer.Ordinal.Equals(parts[0], tenantId))
            .Select(parts => parts[1])
            .Where(value => value.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
}

public sealed class ForbiddenAccessException(string message) : Exception(message);
