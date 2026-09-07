using RJ.Application.Privacy;

namespace RJ.DomainTests;

public sealed class BrazilianTaxIdMaskerTests
{
    [Theory]
    [InlineData("CPF 529.982.247-25", "CPF ***.***.***-**")]
    [InlineData("CPF 52998224725", "CPF ***********")]
    [InlineData("CNPJ 04.252.011/0001-10", "CNPJ **.***.***/****-**")]
    [InlineData("CNPJ 04252011000110", "CNPJ **************")]
    public void Mask_redacts_valid_brazilian_tax_ids_without_changing_length(string input, string expected)
    {
        var result = BrazilianTaxIdMasker.Mask(input);

        Assert.Equal(expected, result);
        Assert.Equal(input.Length, result.Length);
    }

    [Theory]
    [InlineData("CPF 111.111.111-11")]
    [InlineData("CPF 529.982.247-24")]
    [InlineData("CNPJ 11.111.111/1111-11")]
    [InlineData("CNPJ 04.252.011/0001-11")]
    public void Mask_preserves_invalid_tax_id_like_values(string input)
    {
        var result = BrazilianTaxIdMasker.Mask(input);

        Assert.Equal(input, result);
    }

    [Fact]
    public void Mask_preserves_oab_and_cnj_identifiers()
    {
        const string input = "OAB/RS 123456; processo 6003160-36.2026.8.16.0021";

        var result = BrazilianTaxIdMasker.Mask(input);

        Assert.Equal(input, result);
    }

    [Fact]
    public void Mask_redacts_multiple_tax_ids_without_changing_surrounding_text_or_length()
    {
        const string input = "Autor CPF 529.982.247-25; empresa CNPJ 04.252.011/0001-10.";

        var result = BrazilianTaxIdMasker.Mask(input);

        Assert.Equal("Autor CPF ***.***.***-**; empresa CNPJ **.***.***/****-**.", result);
        Assert.Equal(input.Length, result.Length);
    }

    [Fact]
    public void Mask_rejects_null_input()
    {
        Assert.Throws<ArgumentNullException>(() => BrazilianTaxIdMasker.Mask(null!));
    }
}
