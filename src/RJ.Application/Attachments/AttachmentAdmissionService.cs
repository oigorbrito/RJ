using System.Text;
using RJ.Application.Sources;
using RJ.Domain.Cases;

namespace RJ.Application.Attachments;

public interface IAttachmentArtifactReader
{
    Task<ReadOnlyMemory<byte>> ReadAsync(string artifactReference, CancellationToken cancellationToken);
}

public sealed record AttachmentAdmissionReport(
    string CaseId,
    string AttachmentId,
    bool BinaryVerified,
    bool ExtractedTextVerified,
    bool MetadataMatched,
    bool Passed,
    IReadOnlyList<string> Failures,
    ProcessAttachmentContent? AdmittedContent);

public sealed class AttachmentAdmissionService(IAttachmentArtifactReader artifactReader)
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private readonly IAttachmentArtifactReader _artifactReader = artifactReader ?? throw new ArgumentNullException(nameof(artifactReader));

    public async Task<AttachmentAdmissionReport> AdmitAsync(
        LegalCase legalCase,
        AttachmentBinaryArtifact binary,
        AttachmentExtractionEvidence extraction,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(legalCase);
        ArgumentNullException.ThrowIfNull(binary);
        ArgumentNullException.ThrowIfNull(extraction);
        binary.Validate();
        extraction.Validate();

        var failures = new List<string>();
        var metadataMatched = ValidateMetadata(legalCase, binary, extraction, failures);
        var binaryVerified = await VerifyBinaryAsync(binary, failures, cancellationToken);
        var text = await VerifyExtractedTextAsync(extraction, failures, cancellationToken);
        var extractedTextVerified = text is not null;

        ProcessAttachmentContent? admitted = null;
        if (metadataMatched && binaryVerified && extractedTextVerified)
        {
            admitted = new ProcessAttachmentContent(
                binary.CaseId,
                binary.AttachmentId,
                $"attachment:{extraction.ExtractorId}:{extraction.ExtractorVersion}",
                extraction.ExtractedTextReference,
                text!,
                extraction.ExtractedAt);

            _ = ProcessAttachmentContentAdmission.Admit(legalCase, [admitted]);
            if (!StringComparer.Ordinal.Equals(admitted.ContentSha256, extraction.ExtractedTextSha256.ToLowerInvariant()))
            {
                failures.Add("Admitted ProcessAttachmentContent hash does not match extraction evidence.");
                admitted = null;
                extractedTextVerified = false;
            }
        }

        return new AttachmentAdmissionReport(
            binary.CaseId,
            binary.AttachmentId,
            binaryVerified,
            extractedTextVerified,
            metadataMatched,
            failures.Count == 0 && admitted is not null,
            failures,
            admitted);
    }

    private static bool ValidateMetadata(
        LegalCase legalCase,
        AttachmentBinaryArtifact binary,
        AttachmentExtractionEvidence extraction,
        List<string> failures)
    {
        var matched = true;
        if (!StringComparer.Ordinal.Equals(binary.CaseId, legalCase.Id.Value)
            || !StringComparer.Ordinal.Equals(extraction.CaseId, legalCase.Id.Value))
        {
            failures.Add("Attachment evidence case id does not match the canonical legal case.");
            matched = false;
        }

        if (!StringComparer.Ordinal.Equals(binary.AttachmentId, extraction.AttachmentId))
        {
            failures.Add("Binary and extraction evidence refer to different attachment ids.");
            matched = false;
        }

        var metadata = legalCase.Attachments.FirstOrDefault(item => StringComparer.Ordinal.Equals(item.Id, binary.AttachmentId));
        if (metadata is null)
        {
            failures.Add("Attachment binary must correspond to observed attachment metadata.");
            matched = false;
        }
        else if (!StringComparer.OrdinalIgnoreCase.Equals(metadata.Extension.TrimStart('.'), binary.Extension.TrimStart('.')))
        {
            failures.Add("Attachment binary extension does not match observed attachment metadata.");
            matched = false;
        }

        if (!StringComparer.Ordinal.Equals(binary.Sha256.ToLowerInvariant(), extraction.BinarySha256.ToLowerInvariant()))
        {
            failures.Add("Extraction evidence binary hash does not match the acquired binary hash.");
            matched = false;
        }

        return matched;
    }

    private async Task<bool> VerifyBinaryAsync(
        AttachmentBinaryArtifact binary,
        List<string> failures,
        CancellationToken cancellationToken)
    {
        try
        {
            var bytes = await _artifactReader.ReadAsync(binary.SourceReference, cancellationToken);
            if (bytes.Length != binary.ByteLength)
            {
                failures.Add($"Attachment binary length mismatch: expected {binary.ByteLength}, observed {bytes.Length}.");
                return false;
            }

            var actual = AttachmentBinaryArtifact.ComputeSha256(bytes.Span);
            if (!StringComparer.Ordinal.Equals(actual, binary.Sha256.ToLowerInvariant()))
            {
                failures.Add($"Attachment binary SHA-256 mismatch: expected {binary.Sha256.ToLowerInvariant()}, observed {actual}.");
                return false;
            }

            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            failures.Add($"Attachment binary read failed: {exception.GetType().Name}.");
            return false;
        }
    }

    private async Task<string?> VerifyExtractedTextAsync(
        AttachmentExtractionEvidence extraction,
        List<string> failures,
        CancellationToken cancellationToken)
    {
        try
        {
            var bytes = await _artifactReader.ReadAsync(extraction.ExtractedTextReference, cancellationToken);
            string text;
            try
            {
                text = StrictUtf8.GetString(bytes.Span);
            }
            catch (DecoderFallbackException)
            {
                failures.Add("Extracted attachment text is not valid strict UTF-8.");
                return null;
            }

            if (text.Length != extraction.ExtractedTextLength)
            {
                failures.Add($"Extracted attachment text length mismatch: expected {extraction.ExtractedTextLength}, observed {text.Length}.");
                return null;
            }

            var actual = AttachmentBinaryArtifact.ComputeSha256(bytes.Span);
            if (!StringComparer.Ordinal.Equals(actual, extraction.ExtractedTextSha256.ToLowerInvariant()))
            {
                failures.Add($"Extracted attachment text SHA-256 mismatch: expected {extraction.ExtractedTextSha256.ToLowerInvariant()}, observed {actual}.");
                return null;
            }

            if (string.IsNullOrWhiteSpace(text))
            {
                failures.Add("Extracted attachment text cannot be empty.");
                return null;
            }

            return text;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            failures.Add($"Extracted attachment text read failed: {exception.GetType().Name}.");
            return null;
        }
    }
}
