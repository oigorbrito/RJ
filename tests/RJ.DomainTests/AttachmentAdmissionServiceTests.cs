using System.Text;
using RJ.Application.Attachments;
using RJ.Domain.Cases;

namespace RJ.DomainTests;

public sealed class AttachmentAdmissionServiceTests
{
    [Fact]
    public async Task AdmitAsync_verifies_binary_text_and_metadata_before_producing_content()
    {
        var binaryBytes = Encoding.UTF8.GetBytes("binary-fixture");
        var textBytes = Encoding.UTF8.GetBytes("conteudo extraido verificavel");
        var binaryHash = AttachmentBinaryArtifact.ComputeSha256(binaryBytes);
        var textHash = AttachmentBinaryArtifact.ComputeSha256(textBytes);
        var reader = new DictionaryAttachmentReader(new Dictionary<string, byte[]>
        {
            ["binary/att-1.pdf"] = binaryBytes,
            ["text/att-1.txt"] = textBytes
        });
        var service = new AttachmentAdmissionService(reader);

        var report = await service.AdmitAsync(
            LegalCase(),
            new AttachmentBinaryArtifact(
                "case-001",
                "att-1",
                "binary/att-1.pdf",
                "att-1.pdf",
                "application/pdf",
                "pdf",
                binaryHash,
                binaryBytes.Length,
                DateTimeOffset.Parse("2026-09-11T12:00:00-03:00")),
            new AttachmentExtractionEvidence(
                "case-001",
                "att-1",
                binaryHash,
                "parser-test",
                "1.0.0",
                "embedded-text",
                "text/att-1.txt",
                textHash,
                Encoding.UTF8.GetString(textBytes).Length,
                DateTimeOffset.Parse("2026-09-11T12:01:00-03:00"),
                false,
                null,
                null),
            CancellationToken.None);

        Assert.True(report.Passed);
        Assert.True(report.BinaryVerified);
        Assert.True(report.ExtractedTextVerified);
        Assert.True(report.MetadataMatched);
        Assert.Empty(report.Failures);
        Assert.NotNull(report.AdmittedContent);
        Assert.Equal(textHash, report.AdmittedContent!.ContentSha256);
    }

    [Fact]
    public async Task AdmitAsync_fails_when_binary_hash_does_not_match_observed_bytes()
    {
        var binaryBytes = Encoding.UTF8.GetBytes("binary-fixture");
        var textBytes = Encoding.UTF8.GetBytes("conteudo extraido verificavel");
        var textHash = AttachmentBinaryArtifact.ComputeSha256(textBytes);
        var reader = new DictionaryAttachmentReader(new Dictionary<string, byte[]>
        {
            ["binary/att-1.pdf"] = binaryBytes,
            ["text/att-1.txt"] = textBytes
        });
        var service = new AttachmentAdmissionService(reader);
        var wrongHash = new string('a', 64);

        var report = await service.AdmitAsync(
            LegalCase(),
            Binary(wrongHash, binaryBytes.Length),
            Extraction(wrongHash, textHash, Encoding.UTF8.GetString(textBytes).Length),
            CancellationToken.None);

        Assert.False(report.Passed);
        Assert.False(report.BinaryVerified);
        Assert.Contains(report.Failures, item => item.Contains("binary SHA-256 mismatch", StringComparison.Ordinal));
        Assert.Null(report.AdmittedContent);
    }

    [Fact]
    public async Task AdmitAsync_fails_when_extraction_does_not_match_observed_attachment_metadata()
    {
        var binaryBytes = Encoding.UTF8.GetBytes("binary-fixture");
        var textBytes = Encoding.UTF8.GetBytes("conteudo extraido verificavel");
        var binaryHash = AttachmentBinaryArtifact.ComputeSha256(binaryBytes);
        var textHash = AttachmentBinaryArtifact.ComputeSha256(textBytes);
        var reader = new DictionaryAttachmentReader(new Dictionary<string, byte[]>
        {
            ["binary/att-2.pdf"] = binaryBytes,
            ["text/att-2.txt"] = textBytes
        });
        var service = new AttachmentAdmissionService(reader);

        var report = await service.AdmitAsync(
            LegalCase(),
            Binary(binaryHash, binaryBytes.Length) with
            {
                AttachmentId = "att-2",
                SourceReference = "binary/att-2.pdf"
            },
            Extraction(binaryHash, textHash, Encoding.UTF8.GetString(textBytes).Length) with
            {
                AttachmentId = "att-2",
                ExtractedTextReference = "text/att-2.txt"
            },
            CancellationToken.None);

        Assert.False(report.Passed);
        Assert.False(report.MetadataMatched);
        Assert.Contains(report.Failures, item => item.Contains("observed attachment metadata", StringComparison.Ordinal));
        Assert.Null(report.AdmittedContent);
    }

    [Fact]
    public void Extraction_evidence_requires_explicit_ocr_engine_metadata_when_ocr_was_used()
    {
        var evidence = Extraction(new string('a', 64), new string('b', 64), 10) with
        {
            OcrUsed = true,
            OcrEngineId = null,
            OcrEngineVersion = null
        };

        Assert.Throws<InvalidOperationException>(evidence.Validate);
    }

    private static AttachmentBinaryArtifact Binary(string binaryHash, int byteLength) =>
        new(
            "case-001",
            "att-1",
            "binary/att-1.pdf",
            "att-1.pdf",
            "application/pdf",
            "pdf",
            binaryHash,
            byteLength,
            DateTimeOffset.Parse("2026-09-11T12:00:00-03:00"));

    private static AttachmentExtractionEvidence Extraction(string binaryHash, string textHash, int textLength) =>
        new(
            "case-001",
            "att-1",
            binaryHash,
            "parser-test",
            "1.0.0",
            "embedded-text",
            "text/att-1.txt",
            textHash,
            textLength,
            DateTimeOffset.Parse("2026-09-11T12:01:00-03:00"),
            false,
            null,
            null);

    private static LegalCase LegalCase() =>
        new(
            new LegalCaseId("case-001"),
            new LegalCaseCnj("6003160-36.2026.8.16.0021"),
            "Processo de teste",
            "TJPR",
            "INICIAL",
            "ATIVO",
            0,
            null,
            [new LegalCaseParty("Parte", "ACTIVE", "PERSON", null)],
            [],
            [new LegalCaseClassification("1", "Classe")],
            [new LegalCaseSubject("1", "Assunto")],
            [new LegalCaseStep("step-1", DateTimeOffset.Parse("2026-09-01T00:00:00Z"), "Movimento", "source")],
            [new LegalCaseAttachment("att-1", "att-1.pdf", "step-1", "pdf", "available", DateTimeOffset.Parse("2026-09-01T00:00:00Z"))],
            [new LegalCaseFieldProvenance(
                "cnj",
                "source",
                "case.json",
                new string('a', 64),
                "cnj",
                DateTimeOffset.Parse("2026-09-01T00:00:00Z"))]);

    private sealed class DictionaryAttachmentReader(IReadOnlyDictionary<string, byte[]> artifacts) : IAttachmentArtifactReader
    {
        public Task<ReadOnlyMemory<byte>> ReadAsync(string artifactReference, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!artifacts.TryGetValue(artifactReference, out var bytes))
            {
                throw new FileNotFoundException("Attachment test artifact not found.", artifactReference);
            }

            return Task.FromResult<ReadOnlyMemory<byte>>(bytes);
        }
    }
}
