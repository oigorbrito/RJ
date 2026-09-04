using RJ.Domain.Cases;
using RJ.Domain.Documents;

namespace RJ.DomainTests;

public sealed class LegalDocumentTests
{
    private const string ValidHash = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    [Fact]
    public void Constructor_rejects_empty_raw_content()
    {
        Assert.Throws<ArgumentException>(() => new LegalDocument(
            new LegalDocumentId("doc-1"),
            new LegalCaseId("case-1"),
            "source.pdf",
            " ",
            "content",
            ValidHash));
    }

    [Fact]
    public void Constructor_rejects_empty_normalized_content()
    {
        Assert.Throws<ArgumentException>(() => new LegalDocument(
            new LegalDocumentId("doc-1"),
            new LegalCaseId("case-1"),
            "source.pdf",
            "content",
            " ",
            ValidHash));
    }

    [Fact]
    public void Constructor_rejects_invalid_sha256()
    {
        Assert.Throws<ArgumentException>(() => new LegalDocument(
            new LegalDocumentId("doc-1"),
            new LegalCaseId("case-1"),
            "source.pdf",
            "content",
            "content",
            "invalid"));
    }

    [Fact]
    public void Constructor_preserves_raw_content_and_normalizes_source_and_hash()
    {
        var document = new LegalDocument(
            new LegalDocumentId("doc-1"),
            new LegalCaseId("case-1"),
            " source.pdf ",
            "raw\r\ncontent",
            "raw\ncontent",
            ValidHash.ToUpperInvariant());

        Assert.Equal("source.pdf", document.SourceName);
        Assert.Equal("raw\r\ncontent", document.RawContent);
        Assert.Equal("raw\ncontent", document.Content);
        Assert.Equal(ValidHash, document.ContentSha256);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Identifiers_reject_blank_values(string value)
    {
        Assert.Throws<ArgumentException>(() => new LegalCaseId(value));
        Assert.Throws<ArgumentException>(() => new LegalDocumentId(value));
    }
}
