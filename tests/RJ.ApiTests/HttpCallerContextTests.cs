using System.Security.Claims;
using RJ.Api.Security;

namespace RJ.ApiTests;

public sealed class HttpCallerContextTests
{
    [Fact]
    public void Require_rejects_anonymous_principal()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity());

        Assert.Throws<UnauthorizedAccessException>(() => HttpCallerContext.Require(principal));
    }

    [Fact]
    public void Require_rejects_authenticated_principal_without_required_identity_claims()
    {
        var principal = Principal((HttpCallerContext.TenantIdClaim, "tenant-1"));

        Assert.Throws<UnauthorizedAccessException>(() => HttpCallerContext.Require(principal));
    }

    [Fact]
    public void Require_builds_caller_context_only_from_authenticated_claims()
    {
        var principal = Principal(
            (HttpCallerContext.TenantIdClaim, "tenant-1"),
            (HttpCallerContext.SubjectIdClaim, "subject-1"),
            (HttpCallerContext.TenantCaseAccessClaim, "tenant-1:case-1"),
            (HttpCallerContext.TenantCaseAccessClaim, "tenant-1:case-2"),
            (HttpCallerContext.EvidenceSourceClaim, "Judit"),
            (HttpCallerContext.SealedCaseAccessClaim, "true"));

        var caller = HttpCallerContext.Require(principal);

        Assert.Equal("tenant-1", caller.TenantId);
        Assert.Equal("subject-1", caller.SubjectId);
        Assert.Equal(["case-1", "case-2"], caller.AuthorizedCaseIds);
        Assert.Equal(["Judit"], caller.AuthorizedEvidenceSourceNames);
        Assert.True(caller.CanAccessSealedCases);
    }

    [Fact]
    public void RequireCase_rejects_cross_case_access()
    {
        var principal = Principal(
            (HttpCallerContext.TenantIdClaim, "tenant-1"),
            (HttpCallerContext.SubjectIdClaim, "subject-1"),
            (HttpCallerContext.TenantCaseAccessClaim, "tenant-1:case-1"));

        Assert.Throws<ForbiddenAccessException>(() => HttpCallerContext.RequireCase(principal, "case-2"));
    }

    [Fact]
    public void RequireCase_keeps_tenant_identity_from_claims_and_cannot_be_overridden_by_case_input()
    {
        var principal = Principal(
            (HttpCallerContext.TenantIdClaim, "tenant-1"),
            (HttpCallerContext.SubjectIdClaim, "subject-1"),
            (HttpCallerContext.TenantCaseAccessClaim, "tenant-1:case-1"));

        var caller = HttpCallerContext.RequireCase(principal, "case-1");

        Assert.Equal("tenant-1", caller.TenantId);
        Assert.Equal("subject-1", caller.SubjectId);
        Assert.DoesNotContain("tenant-2", caller.AuthorizedCaseIds);
    }

    [Fact]
    public void RequireCase_rejects_case_claim_scoped_to_another_tenant()
    {
        var principal = Principal(
            (HttpCallerContext.TenantIdClaim, "tenant-2"),
            (HttpCallerContext.SubjectIdClaim, "subject-2"),
            (HttpCallerContext.TenantCaseAccessClaim, "tenant-1:case-1"));

        Assert.Throws<UnauthorizedAccessException>(() => HttpCallerContext.Require(principal));
    }

    private static ClaimsPrincipal Principal(params (string Type, string Value)[] claims) =>
        new(new ClaimsIdentity(
            claims.Select(item => new Claim(item.Type, item.Value)),
            authenticationType: "test"));
}
