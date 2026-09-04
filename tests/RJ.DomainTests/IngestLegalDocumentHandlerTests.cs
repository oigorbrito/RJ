using RJ.Application.Ingestion;
using RJ.Domain.Documents;

namespace RJ.DomainTests;

public sealed class IngestLegalDocumentHandlerTests
{
    private const string RawHash = "9854ef53c4c0331529ecca1c5a53020d5d5c2f74c0159431b7c3dc91b6d674fc";

    [Fact]
    public async Task Handler_computes_hash_from_raw_content_and_normalizes_line_endings()
    {
        var writer = new CapturingWriter();
        var handler = new IngestLegalDocumentHandler(writer);
        var command = new IngestLegalDocumentCommand(
            "case-1",
            "doc-1",
            " source.pdf ",
            "raw\r\ncontent");

        await handler.HandleAsync(command, CancellationToken.None);

        var stored = Assert.IsType<LegalDocument>(writer.Stored);
        Assert.Equal("case-1", stored.CaseId.Value);
        Assert.Equal("doc-1", stored.Id.Value);
        Assert.Equal("source.pdf", stored.SourceName);
        Assert.Equal("raw\r\ncontent", stored.RawContent);
        Assert.Equal("raw\ncontent", stored.Content);
        Assert.Equal(RawHash, stored.ContentSha256);
    }

    [Fact]
    public async Task Handler_does_not_write_when_document_is_invalid()
    {
        var writer = new CapturingWriter();
        var handler = new IngestLegalDocumentHandler(writer);
        var command = new IngestLegalDocumentCommand(
            "case-1",
            "doc-1",
            "source.pdf",
            " ");

        await Assert.ThrowsAsync<ArgumentException>(() => handler.HandleAsync(command, CancellationToken.None));

        Assert.Null(writer.Stored);
    }

    private sealed class CapturingWriter : ILegalDocumentWriter
    {
        public LegalDocument? Stored { get; private set; }

        public Task StoreAsync(LegalDocument document, CancellationToken cancellationToken)
        {
            Stored = document;
            return Task.CompletedTask;
        }
    }
}
