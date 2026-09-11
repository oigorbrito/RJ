using System.Text.Json;

namespace RJ.Application.Attachments;

public sealed record AttachmentAdmissionManifest(
    string FormatVersion,
    string CanonicalSourceReference,
    string CanonicalSourceSha256,
    DateTimeOffset CanonicalSourceObservedAt,
    AttachmentBinaryArtifact Binary,
    AttachmentExtractionEvidence Extraction)
{
    public const string SupportedFormatVersion = "rjudi-attachment-admission-v1";

    public static AttachmentAdmissionManifest Parse(ReadOnlySpan<byte> utf8Json)
    {
        if (utf8Json.IsEmpty)
        {
            throw new ArgumentException("Attachment admission manifest cannot be empty.", nameof(utf8Json));
        }

        return JsonSerializer.Deserialize<AttachmentAdmissionManifest>(utf8Json, JsonOptions)
            ?? throw new InvalidOperationException("Attachment admission manifest produced no document.");
    }

    public AttachmentAdmissionManifest Validate()
    {
        if (!StringComparer.Ordinal.Equals(FormatVersion, SupportedFormatVersion))
        {
            throw new InvalidOperationException($"Unsupported attachment admission format '{FormatVersion}'.");
        }

        AttachmentBinaryArtifact.Require(CanonicalSourceReference, nameof(CanonicalSourceReference));
        AttachmentBinaryArtifact.ValidateSha256(CanonicalSourceSha256, nameof(CanonicalSourceSha256));
        if (CanonicalSourceObservedAt == default)
        {
            throw new InvalidOperationException("Canonical source observed-at timestamp must be recorded.");
        }

        ArgumentNullException.ThrowIfNull(Binary);
        ArgumentNullException.ThrowIfNull(Extraction);
        Binary.Validate();
        Extraction.Validate();

        var canonicalCaseId = Path.GetFileNameWithoutExtension(CanonicalSourceReference);
        if (!StringComparer.Ordinal.Equals(canonicalCaseId, Binary.CaseId))
        {
            throw new InvalidOperationException(
                "Canonical source reference file name must derive the same case id as the attachment binary evidence.");
        }

        return this;
    }

    public static string ComputeSha256(ReadOnlySpan<byte> bytes) => AttachmentBinaryArtifact.ComputeSha256(bytes);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };
}
