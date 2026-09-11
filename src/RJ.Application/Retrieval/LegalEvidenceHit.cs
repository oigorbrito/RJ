namespace RJ.Application.Retrieval;

public sealed record LegalEvidenceHit(
    string CaseId,
    string DocumentId,
    string SourceName,
    string ContentSha256,
    string Excerpt,
    SourcePosition Position,
    float Rank)
{
    public string CaseId { get; } = Require(CaseId, nameof(CaseId));

    public string DocumentId { get; } = Require(DocumentId, nameof(DocumentId));

    public string SourceName { get; } = Require(SourceName, nameof(SourceName));

    public string ContentSha256 { get; } = RequireSha256(ContentSha256, nameof(ContentSha256));

    public string Excerpt { get; } = Require(Excerpt, nameof(Excerpt));

    public SourcePosition Position { get; } = Position ?? throw new ArgumentNullException(nameof(Position));

    public float Rank { get; } = RequireRank(Rank, nameof(Rank));

    private static string Require(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Evidence value cannot be empty.", parameterName);
        }

        return value.Trim();
    }

    private static string RequireSha256(string value, string parameterName)
    {
        var normalized = Require(value, parameterName).ToLowerInvariant();
        if (normalized.Length != 64 || normalized.Any(character => !Uri.IsHexDigit(character)))
        {
            throw new ArgumentException("Evidence content hash must be a SHA-256 hex digest.", parameterName);
        }

        return normalized;
    }

    private static float RequireRank(float value, string parameterName)
    {
        if (float.IsNaN(value) || float.IsInfinity(value))
        {
            throw new ArgumentException("Evidence rank must be finite.", parameterName);
        }

        return value;
    }
}
