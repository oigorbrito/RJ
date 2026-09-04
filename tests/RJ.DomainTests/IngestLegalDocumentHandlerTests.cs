using RJ.Application.Ingestion;
using RJ.Domain.Cases;
using RJ.Domain.Documents;

namespace RJ.DomainTests;

public sealed class IngestLegalDocumentHandlerTests
{
    private const string ValidHash = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    [Fact]
    public async Task Handler_stores_validated_document()
    {
        var writer = new CapturingWriter();
        var handler = new IngestLegalDocumentHandler(writer);
        var command = new IngestLegalDocumentCommand(
            new LegalCaseId("case-1"),
            new LegalDocumentId("doc-1"),
            " source.pdf ",
            "content",
            ValidHash.ToUpperInvariant());

        await handler.HandleAsync(command, CancellationToken.None);

        var stored = Assert.IsType<LegalDocument>(writer.Stored);
        Assert.Equal("case-1", stored.CaseId.Value);
        Assert.Equal("doc-1", stored.Id.Value);
        Assert.Equal("source.pdf", stored.SourceName);
        Assert.Equal(ValidHash, stored.ContentSha256);
    }

    [Fact]
    public async Task Handler_does_not_write_when_document_is_invalid()
    {
        var writer = new CapturingWriter();
        var handler = new IngestLegalDocumentHandler(writer);
        var command = new IngestLegalDocumentCommand(
            new LegalCaseId("case-1"),
            new LegalDocumentId("doc-1"),
            "source.pdf",
            " ",
            ValidHash);

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
