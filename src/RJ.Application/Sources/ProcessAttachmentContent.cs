using System.Security.Cryptography;
using System.Text;
using RJ.Domain.Cases;

namespace RJ.Application.Sources;

public sealed record ProcessAttachmentContent(
    string CaseId,
    string AttachmentId,
    string SourceName,
    string SourceReference,
    string ExtractedText,
    DateTimeOffset ObservedAt)
{
    public string CaseId { get; } = Require(CaseId, nameof(CaseId));

    public string AttachmentId { get; } = Require(AttachmentId, nameof(AttachmentId));

    public string SourceName { get; } = Require(SourceName, nameof(SourceName));

    public string SourceReference { get; } = Require(SourceReference, nameof(SourceReference));

    public string ExtractedText { get; } = Require(ExtractedText, nameof(ExtractedText));

    public DateTimeOffset ObservedAt { get; } = RequireObservedAt(ObservedAt, nameof(ObservedAt));

    public string ContentSha256 { get; } = ComputeSha256(ExtractedText);

    public LegalCaseFieldProvenance ToProvenance() =>
        new(
            $"attachments[{AttachmentId}].content",
            SourceName,
            SourceReference,
            ContentSha256,
            SourceReference,
            ObservedAt);

    private static string Require(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Attachment content value cannot be empty.", parameterName);
        }

        return value.Trim();
    }

    private static DateTimeOffset RequireObservedAt(DateTimeOffset value, string parameterName)
    {
        if (value == default)
        {
            throw new ArgumentException("Attachment content observed instant cannot be empty.", parameterName);
        }

        return value;
    }

    private static string ComputeSha256(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }
}
