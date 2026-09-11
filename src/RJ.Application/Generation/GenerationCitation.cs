namespace RJ.Application.Generation;

public sealed record GenerationCitation(
    string DocumentId,
    string ContentSha256,
    int StartOffset,
    int Length)
{
    public string DocumentId { get; } = Require(DocumentId, nameof(DocumentId));

    public string ContentSha256 { get; } = RequireSha256(ContentSha256, nameof(ContentSha256));

    public int StartOffset { get; } = RequireNonNegative(StartOffset, nameof(StartOffset));

    public int Length { get; } = RequirePositive(Length, nameof(Length));

    private static string Require(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Generation citation value cannot be empty.", parameterName);
        }

        return value.Trim();
    }

    private static string RequireSha256(string value, string parameterName)
    {
        var normalized = Require(value, parameterName).ToLowerInvariant();
        if (normalized.Length != 64 || normalized.Any(character => !Uri.IsHexDigit(character)))
        {
            throw new ArgumentException("Generation citation content hash must be a SHA-256 hex digest.", parameterName);
        }

        return normalized;
    }

    private static int RequireNonNegative(int value, string parameterName)
    {
        if (value < 0)
        {
            throw new ArgumentOutOfRangeException(parameterName, value, "Generation citation start offset cannot be negative.");
        }

        return value;
    }

    private static int RequirePositive(int value, string parameterName)
    {
        if (value <= 0)
        {
            throw new ArgumentOutOfRangeException(parameterName, value, "Generation citation length must be positive.");
        }

        return value;
    }
}
