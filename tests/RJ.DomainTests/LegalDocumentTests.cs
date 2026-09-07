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
        Assert.Throws<ArgumentException>(() => new LegalCaseCnj(value));
        Assert.Throws<ArgumentException>(() => new LegalDocumentId(value));
    }

    [Theory]
    [InlineData("6003160-36.2026.8.16.0021")]
    [InlineData("60031603620268160021")]
    [InlineData(" 6003160-36.2026.8.16.0021 ")]
    public void Legal_case_cnj_accepts_valid_process_numbers(string value)
    {
        var cnj = new LegalCaseCnj(value);

        Assert.Equal("6003160-36.2026.8.16.0021", cnj.Value);
        Assert.Equal("60031603620268160021", cnj.Digits);
        Assert.Equal(cnj.Value, cnj.ToString());
    }

    [Theory]
    [InlineData("6003160-00.2026.8.16.0021")]
    [InlineData("6003160-36.2026.8.16")]
    [InlineData("6003160-36.2026.8.16.A021")]
    public void Legal_case_cnj_rejects_invalid_process_numbers(string value)
    {
        Assert.Throws<ArgumentException>(() => new LegalCaseCnj(value));
    }
}
