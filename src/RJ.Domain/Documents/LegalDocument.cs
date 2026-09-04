using RJ.Domain.Cases;

namespace RJ.Domain.Documents;

public sealed class LegalDocument
{
    public LegalDocument(
        LegalDocumentId id,
        LegalCaseId caseId,
        string sourceName,
        string content,
        string contentSha256)
    {
        if (string.IsNullOrWhiteSpace(sourceName))
        {
            throw new ArgumentException("Source name cannot be empty.", nameof(sourceName));
        }

        if (string.IsNullOrWhiteSpace(content))
        {
            throw new ArgumentException("Document content cannot be empty.", nameof(content));
        }

        if (!IsSha256Hex(contentSha256))
        {
            throw new ArgumentException("Content SHA-256 must contain exactly 64 hexadecimal characters.", nameof(contentSha256));
        }

        Id = id;
        CaseId = caseId;
        SourceName = sourceName.Trim();
        Content = content;
        ContentSha256 = contentSha256.ToLowerInvariant();
    }

    public LegalDocumentId Id { get; }

    public LegalCaseId CaseId { get; }

    public string SourceName { get; }

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
