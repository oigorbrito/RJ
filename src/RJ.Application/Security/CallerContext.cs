using RJ.Domain.Cases;

namespace RJ.Application.Security;

public sealed record CallerContext(
    string TenantId,
    string SubjectId,
    IReadOnlyList<string> AuthorizedCaseIds,
    bool CanAccessSealedCases,
    IReadOnlyList<string>? AuthorizedEvidenceSourceNames = null)
{
    public string TenantId { get; } = Require(TenantId, nameof(TenantId));

    public string SubjectId { get; } = Require(SubjectId, nameof(SubjectId));

    public IReadOnlyList<string> AuthorizedCaseIds { get; } =
        (AuthorizedCaseIds ?? throw new ArgumentNullException(nameof(AuthorizedCaseIds)))
        .Select(value => Require(value, nameof(AuthorizedCaseIds)))
        .ToArray();

    public IReadOnlyList<string>? AuthorizedEvidenceSourceNames { get; } =
        AuthorizedEvidenceSourceNames?.Select(value => Require(value, nameof(AuthorizedEvidenceSourceNames))).ToArray();

    public bool IsAuthorizedFor(LegalCaseId caseId)
    {
        ArgumentNullException.ThrowIfNull(caseId);
        return AuthorizedCaseIds.Contains(caseId.Value, StringComparer.Ordinal);
    }

    public bool IsAuthorizedForCase(string caseId)
    {
        return IsAuthorizedFor(new LegalCaseId(Require(caseId, nameof(caseId))));
    }

    public bool IsAuthorizedForEvidenceSource(string sourceName)
    {
        var name = Require(sourceName, nameof(sourceName));
        return AuthorizedEvidenceSourceNames is null
            || AuthorizedEvidenceSourceNames.Contains(name, StringComparer.Ordinal);
    }

    private static string Require(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Security context value cannot be empty.", parameterName);
        }

        return value.Trim();
    }
}
