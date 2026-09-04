using RJ.Domain.Cases;

namespace RJ.Domain.Documents;

public sealed class LegalDocument
{
    public LegalDocument(
        LegalDocumentId id,
        LegalCaseId caseId,
        string sourceName,
        string rawContent,
        string normalizedContent,
        string contentSha256)
    {
        if (string.IsNullOrWhiteSpace(sourceName))
        {
            throw new ArgumentException("Source name cannot be empty.", nameof(sourceName));
        }

        if (string.IsNullOrWhiteSpace(rawContent))
        {
            throw new ArgumentException("Raw document content cannot be empty.", nameof(rawContent));
        }

        if (string.IsNullOrWhiteSpace(normalizedContent))
        {
            throw new ArgumentException("Normalized document content cannot be empty.", nameof(normalizedContent));
        }

        if (!IsSha256Hex(contentSha256))
        {
            throw new ArgumentException("Content SHA-256 must contain exactly 64 hexadecimal characters.", nameof(contentSha256));
        }

        Id = id;
        CaseId = caseId;
        SourceName = sourceName.Trim();
        RawContent = rawContent;
        Content = normalizedContent;
        ContentSha256 = contentSha256.ToLowerInvariant();
    }

    public LegalDocumentId Id { get; }

    public LegalCaseId CaseId { get; }

    public string SourceName { get; }

    public string RawContent { get; }

    public string Content { get; }

    public string ContentSha256 { get; }

    private static bool IsSha256Hex(string value)
    {
        if (value.Length != 64)
        {
            return false;
        }

        foreach (var character in value)
        {
            if (!Uri.IsHexDigit(character))
            {
                return false;
            }
        }

        return true;
    }
}
