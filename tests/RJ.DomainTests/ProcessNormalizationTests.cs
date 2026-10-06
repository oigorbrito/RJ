using System.Globalization;
using RJ.Application.Sources;

namespace RJ.DomainTests;

public sealed class ProcessNormalizationTests
{
    [Fact]
    public void Normalize_step_content_trims_and_collapses_whitespace_without_changing_raw_source()
    {
        const string raw = "  6 - EXPEDIDA/CERTIFICADA A INTIMAÇÃO ELETRÔNICA   - AUDIÊNCIA\r\nREFER.  AO EVENTO 4  ";

        var normalized = ProcessNormalization.NormalizeStepContent(raw);

        Assert.Equal("6 - EXPEDIDA/CERTIFICADA A INTIMAÇÃO ELETRÔNICA - AUDIÊNCIA REFER. AO EVENTO 4", normalized);
        Assert.Contains("\r\n", raw, StringComparison.Ordinal);
    }

    [Fact]
    public void Explicit_correction_preserves_raw_observed_value_and_reason()
    {
        var correction = ProcessNormalization.CorrectObservedValue(
            "DIREITO DO TRABALHO",
            "DIREITO CIVIL",
            "subjects contain DIREITO CIVIL and no labor subject");

        Assert.Equal("DIREITO DO TRABALHO", correction.RawValue);
        Assert.Equal("DIREITO CIVIL", correction.NormalizedValue);
        Assert.Equal("subjects contain DIREITO CIVIL and no labor subject", correction.Reason);
    }

    [Fact]
    public void Sao_paulo_display_uses_source_instant_without_mutating_it()
    {
        var instant = DateTimeOffset.Parse("2026-09-01T20:33:39.000Z", CultureInfo.InvariantCulture);

        var display = ProcessNormalization.FormatSaoPauloDateTime(instant);

        Assert.Equal("03/03/2027 15:00", ProcessNormalization.FormatSaoPauloDateTime(
            DateTimeOffset.Parse("2027-03-03T18:00:00.000Z", CultureInfo.InvariantCulture)));
        Assert.Equal("2026-09-01T20:33:39.0000000+00:00", instant.ToString("O", CultureInfo.InvariantCulture));
        Assert.Equal("01/09/2026 17:33", display);
    }
}
