using System.Text;
using RJ.Application.Normalization;

namespace RJ.DomainTests;

public sealed class ProcessTextNormalizerTests
{
    [Fact]
    public void Normalize_canonicalizes_line_endings_and_trims_only_line_edges()
    {
        const string input = "\r\n  primeira linha  \r\nsegunda   linha\t \rterceira linha\n\n";

        var result = ProcessTextNormalizer.Normalize(input);

        Assert.Equal("  primeira linha\nsegunda   linha\nterceira linha", result);
    }

    [Fact]
    public void Normalize_preserves_internal_blank_lines_and_punctuation()
    {
        const string input = "Decisão: deferida.\n\nValor: R$ 30.000,00.";

        var result = ProcessTextNormalizer.Normalize(input);

        Assert.Equal(input, result);
    }

    [Fact]
    public void Normalize_uses_unicode_nfc_without_changing_visible_text()
    {
        var decomposed = "Sa\u0303o Paulo";
        var expected = "São Paulo".Normalize(NormalizationForm.FormC);

        var result = ProcessTextNormalizer.Normalize(decomposed);

        Assert.Equal(expected, result);
        Assert.True(result.IsNormalized(NormalizationForm.FormC));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\r\n\t \r\n")]
    public void Normalize_returns_empty_for_whitespace_only_input(string input)
    {
        var result = ProcessTextNormalizer.Normalize(input);

        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void Normalize_is_idempotent()
    {
        const string input = "\r\nMovimentação 1  \r\n\r\nMovimentação 2\t\r\n";

        var once = ProcessTextNormalizer.Normalize(input);
        var twice = ProcessTextNormalizer.Normalize(once);

        Assert.Equal(once, twice);
    }

    [Fact]
    public void Normalize_rejects_null_input()
    {
        Assert.Throws<ArgumentNullException>(() => ProcessTextNormalizer.Normalize(null!));
    }
}
