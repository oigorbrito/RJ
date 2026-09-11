using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using RJ.Api;
using RJ.Application.Security;

namespace RJ.ApiTests;

public sealed class ProcessSummarySecurityBoundaryTests
{
    [Fact]
    public void Resolver_rejects_unauthenticated_principal()
    {
        var resolver = new ClaimsProcessSummaryCallerContextResolver();

        var resolved = resolver.TryResolve(new ClaimsPrincipal(new ClaimsIdentity()), out var caller);

        Assert.False(resolved);
        Assert.Null(caller);
    }

    [Fact]
    public void Resolver_rejects_authenticated_principal_without_required_identity_claims()
    {
        var resolver = new ClaimsProcessSummaryCallerContextResolver();
        var principal = Principal(
            new Claim(ClaimTypes.NameIdentifier, "subject-1"));

        var resolved = resolver.TryResolve(principal, out var caller);

        Assert.False(resolved);
        Assert.Null(caller);
    }

    [Fact]
    public void Resolver_builds_fail_closed_caller_from_authenticated_claims()
    {
        var resolver = new ClaimsProcessSummaryCallerContextResolver();
        var principal = Principal(
            new Claim(ClaimTypes.NameIdentifier, "subject-1"),
            new Claim(ClaimsProcessSummaryCallerContextResolver.TenantIdClaim, "tenant-1"),
            new Claim(ClaimsProcessSummaryCallerContextResolver.CaseIdClaim, "case-1"),
            new Claim(ClaimsProcessSummaryCallerContextResolver.SealedCaseAccessClaim, "true"));

        var resolved = resolver.TryResolve(principal, out var caller);

        Assert.True(resolved);
        Assert.NotNull(caller);
        Assert.Equal("tenant-1", caller.TenantId);
        Assert.Equal("subject-1", caller.SubjectId);
        Assert.Equal(["case-1"], caller.AuthorizedCaseIds);
        Assert.True(caller.CanAccessSealedCases);
        Assert.NotNull(caller.AuthorizedEvidenceSourceNames);
        Assert.Empty(caller.AuthorizedEvidenceSourceNames);
        Assert.False(caller.IsAuthorizedForEvidenceSource("Judit"));
    }

    [Fact]
    public void Access_store_isolates_job_by_tenant_subject_and_case_scope()
    {
        var store = new InMemoryProcessSummaryJobAccessStore();
        var owner = Caller("tenant-1", "subject-1", "case-1");

        Assert.True(store.TryBind("job-1", owner, "case-1"));
        Assert.True(store.CanAccess("job-1", owner));
        Assert.False(store.CanAccess("job-1", Caller("tenant-2", "subject-1", "case-1")));
        Assert.False(store.CanAccess("job-1", Caller("tenant-1", "subject-2", "case-1")));
        Assert.False(store.CanAccess("job-1", Caller("tenant-1", "subject-1", "case-2")));
    }

    [Fact]
    public void Access_store_rejects_rebinding_job_to_different_scope()
    {
        var store = new InMemoryProcessSummaryJobAccessStore();

        Assert.True(store.TryBind("job-1", Caller("tenant-1", "subject-1", "case-1"), "case-1"));
        Assert.True(store.TryBind("job-1", Caller("tenant-1", "subject-1", "case-1"), "case-1"));
        Assert.False(store.TryBind("job-1", Caller("tenant-2", "subject-1", "case-1"), "case-1"));
    }

    [Fact]
    public void Public_http_request_does_not_accept_identity_or_acl_fields()
    {
        var properties = typeof(ProcessSummaryHttpRequest).GetProperties()
            .Select(property => property.Name)
            .ToHashSet(StringComparer.Ordinal);

        Assert.DoesNotContain("TenantId", properties);
        Assert.DoesNotContain("SubjectId", properties);
        Assert.DoesNotContain("AuthorizedCaseIds", properties);
        Assert.DoesNotContain("CanAccessSealedCases", properties);
        Assert.DoesNotContain("AuthorizedEvidenceSourceNames", properties);
    }

    [Fact]
    public async Task Submit_boundary_returns_401_before_processing_for_unauthenticated_caller()
    {
        var context = new DefaultHttpContext();
        var result = await ProcessSummaryEndpoint.SubmitAuthenticatedAsync(
            context,
            null!,
            new ClaimsProcessSummaryCallerContextResolver(),
            new InMemoryProcessSummaryJobAccessStore(),
            null!,
            CancellationToken.None);

        Assert.IsType<UnauthorizedHttpResult>(result);
    }

    [Fact]
    public void Read_boundary_hides_unbound_job_from_authenticated_caller()
    {
        var context = new DefaultHttpContext
        {
            User = Principal(
                new Claim(ClaimTypes.NameIdentifier, "subject-1"),
                new Claim(ClaimsProcessSummaryCallerContextResolver.TenantIdClaim, "tenant-1"),
                new Claim(ClaimsProcessSummaryCallerContextResolver.CaseIdClaim, "case-1"),
                new Claim(ClaimsProcessSummaryCallerContextResolver.EvidenceSourceClaim, "Judit"))
        };

        var result = ProcessSummaryEndpoint.GetJobAuthenticated(
            context,
            "job-missing",
            new ClaimsProcessSummaryCallerContextResolver(),
            new InMemoryProcessSummaryJobAccessStore(),
            null!);

        Assert.IsType<NotFound>(result);
    }

    private static ClaimsPrincipal Principal(params Claim[] claims) =>
        new(new ClaimsIdentity(claims, "test"));

    private static CallerContext Caller(string tenantId, string subjectId, string caseId) =>
        new(tenantId, subjectId, [caseId], false, ["Judit"]);
}
