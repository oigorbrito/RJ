using System.Security.Cryptography;
using System.Text;

namespace RJ.Application.Attachments;

public sealed record AttachmentBinaryArtifact(
    string CaseId,
    string AttachmentId,
    string SourceReference,
    string FileName,
    string MediaType,
    string Extension,
    string Sha256,
    long ByteLength,
    DateTimeOffset AcquiredAt)
{
    public AttachmentBinaryArtifact Validate()
    {
        Require(CaseId, nameof(CaseId));
        Require(AttachmentId, nameof(AttachmentId));
        Require(SourceReference, nameof(SourceReference));
        Require(FileName, nameof(FileName));
        Require(MediaType, nameof(MediaType));
        Require(Extension, nameof(Extension));
        ValidateSha256(Sha256, nameof(Sha256));
        if (ByteLength <= 0)
        {
            throw new InvalidOperationException("Attachment binary byte length must be positive.");
        }

        if (AcquiredAt == default)
        {
            throw new InvalidOperationException("Attachment acquisition timestamp must be recorded.");
        }

        return this;
    }

    public static string ComputeSha256(ReadOnlySpan<byte> bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    internal static string Require(string value, string field)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"Required attachment field '{field}' cannot be empty.");
        }

        return value.Trim();
    }

    internal static string ValidateSha256(string value, string field)
    {
        var normalized = Require(value, field).ToLowerInvariant();
        if (normalized.Length != 64 || normalized.Any(character => !Uri.IsHexDigit(character)))
        {
            throw new InvalidOperationException($"Attachment field '{field}' must contain exactly 64 hexadecimal SHA-256 characters.");
        }

        return normalized;
    }
}

public sealed record AttachmentExtractionEvidence(
    string CaseId,
    string AttachmentId,
    string BinarySha256,
    string ExtractorId,
    string ExtractorVersion,
    string Method,
    string ExtractedTextReference,
    string ExtractedTextSha256,
    int ExtractedTextLength,
    DateTimeOffset ExtractedAt,
    bool OcrUsed,
    string? OcrEngineId,
    string? OcrEngineVersion)
{
    public AttachmentExtractionEvidence Validate()
    {
        AttachmentBinaryArtifact.Require(CaseId, nameof(CaseId));
        AttachmentBinaryArtifact.Require(AttachmentId, nameof(AttachmentId));
        AttachmentBinaryArtifact.ValidateSha256(BinarySha256, nameof(BinarySha256));
        AttachmentBinaryArtifact.Require(ExtractorId, nameof(ExtractorId));
        AttachmentBinaryArtifact.Require(ExtractorVersion, nameof(ExtractorVersion));
        AttachmentBinaryArtifact.Require(Method, nameof(Method));
        AttachmentBinaryArtifact.Require(ExtractedTextReference, nameof(ExtractedTextReference));
        AttachmentBinaryArtifact.ValidateSha256(ExtractedTextSha256, nameof(ExtractedTextSha256));
        if (ExtractedTextLength <= 0)
        {
            throw new InvalidOperationException("Extracted text length must be positive.");
        }

        if (ExtractedAt == default)
        {
            throw new InvalidOperationException("Attachment extraction timestamp must be recorded.");
        }

        if (OcrUsed)
        {
            AttachmentBinaryArtifact.Require(OcrEngineId ?? string.Empty, nameof(OcrEngineId));
            AttachmentBinaryArtifact.Require(OcrEngineVersion ?? string.Empty, nameof(OcrEngineVersion));
        }
        else if (!string.IsNullOrWhiteSpace(OcrEngineId) || !string.IsNullOrWhiteSpace(OcrEngineVersion))
        {
            throw new InvalidOperationException("OCR engine metadata cannot be present when OCR was not used.");
        }

        return this;
    }
}

public interface IAttachmentTextExtractor
{
    string ExtractorId { get; }
    string ExtractorVersion { get; }
    Task<AttachmentTextExtractionResult> ExtractAsync(
        AttachmentBinaryArtifact artifact,
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken);
}

public sealed record AttachmentTextExtractionResult(
    string Text,
    string Method,
    bool OcrUsed,
    string? OcrEngineId,
    string? OcrEngineVersion)
{
    public AttachmentTextExtractionResult Validate()
    {
        if (string.IsNullOrWhiteSpace(Text))
        {
            throw new InvalidOperationException("Attachment extraction cannot admit empty text.");
        }

        AttachmentBinaryArtifact.Require(Method, nameof(Method));
        if (OcrUsed)
        {
            AttachmentBinaryArtifact.Require(OcrEngineId ?? string.Empty, nameof(OcrEngineId));
            AttachmentBinaryArtifact.Require(OcrEngineVersion ?? string.Empty, nameof(OcrEngineVersion));
        }
        else if (!string.IsNullOrWhiteSpace(OcrEngineId) || !string.IsNullOrWhiteSpace(OcrEngineVersion))
        {
            throw new InvalidOperationException("OCR engine metadata cannot be present when OCR was not used.");
        }

        return this;
    }

    public string ComputeTextSha256() => AttachmentBinaryArtifact.ComputeSha256(Encoding.UTF8.GetBytes(Text));
}
