using System.Security.Claims;
using RJ.Application.Security;

namespace RJ.Api;

public interface IProcessSummaryCallerContextResolver
{
    bool TryResolve(ClaimsPrincipal principal, out CallerContext? caller);
}

public sealed class ClaimsProcessSummaryCallerContextResolver : IProcessSummaryCallerContextResolver
{
    public const string TenantIdClaim = "rjudi:tenant_id";
    public const string CaseIdClaim = "rjudi:case_id";
    public const string SealedCaseAccessClaim = "rjudi:sealed_access";
    public const string EvidenceSourceClaim = "rjudi:evidence_source";

    public bool TryResolve(ClaimsPrincipal principal, out CallerContext? caller)
    {
        ArgumentNullException.ThrowIfNull(principal);
        caller = null;

        if (principal.Identity?.IsAuthenticated != true)
        {
            return false;
        }

        var tenantId = principal.FindFirst(TenantIdClaim)?.Value;
        var subjectId = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrWhiteSpace(tenantId) || string.IsNullOrWhiteSpace(subjectId))
        {
            return false;
        }

        var caseIds = principal.FindAll(CaseIdClaim)
            .Select(claim => claim.Value)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToArray();
        var evidenceSources = principal.FindAll(EvidenceSourceClaim)
            .Select(claim => claim.Value)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToArray();
        var canAccessSealedCases = bool.TryParse(
            principal.FindFirst(SealedCaseAccessClaim)?.Value,
            out var sealedAccess)
            && sealedAccess;

        try
        {
            caller = new CallerContext(
                tenantId,
                subjectId,
                caseIds,
                canAccessSealedCases,
                evidenceSources);
            return true;
        }
        catch (ArgumentException)
        {
            caller = null;
            return false;
        }
    }
}

public sealed class InMemoryProcessSummaryJobAccessStore : IProcessSummaryJobAccessStore
{
    private readonly Dictionary<string, ProcessSummaryJobAccessScope> scopes = new(StringComparer.Ordinal);
    private readonly object sync = new();

    public Task<bool> TryBindAsync(
        string jobId,
        CallerContext caller,
        string caseId,
        CancellationToken cancellationToken)
    {
        var normalizedJobId = Require(jobId, nameof(jobId));
        var normalizedCaseId = Require(caseId, nameof(caseId));
        ArgumentNullException.ThrowIfNull(caller);
        cancellationToken.ThrowIfCancellationRequested();
        var candidate = new ProcessSummaryJobAccessScope(
            caller.TenantId,
            caller.SubjectId,
            normalizedCaseId);

        lock (sync)
        {
            if (scopes.TryGetValue(normalizedJobId, out var existing))
            {
                return Task.FromResult(existing == candidate);
            }

            scopes.Add(normalizedJobId, candidate);
            return Task.FromResult(true);
        }
    }

    public Task<bool> CanAccessAsync(
        string jobId,
        CallerContext caller,
        CancellationToken cancellationToken)
    {
        var normalizedJobId = Require(jobId, nameof(jobId));
        ArgumentNullException.ThrowIfNull(caller);
        cancellationToken.ThrowIfCancellationRequested();

        lock (sync)
        {
            if (!scopes.TryGetValue(normalizedJobId, out var scope))
            {
                return Task.FromResult(false);
            }

            return Task.FromResult(
                StringComparer.Ordinal.Equals(scope.TenantId, caller.TenantId)
                && StringComparer.Ordinal.Equals(scope.SubjectId, caller.SubjectId)
                && caller.AuthorizedCaseIds.Contains(scope.CaseId, StringComparer.Ordinal));
        }
    }

    private static string Require(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Security boundary value cannot be empty.", parameterName);
        }

        return value.Trim();
    }

    private sealed record ProcessSummaryJobAccessScope(
        string TenantId,
        string SubjectId,
        string CaseId);
}
