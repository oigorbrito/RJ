using System.Globalization;
using System.Text;
using RJ.Application.Sources;

namespace RJ.DomainTests;

public sealed class JuditProcessSourceAdapterTests
{
    private static readonly string RepoRoot = FindRepoRoot();
    private static readonly string FixturePath = Path.Combine(RepoRoot, "tests", "fixtures", "rj", "response_60031603620268160021_1.json");
    private static readonly DateTimeOffset ObservedAt =
        DateTimeOffset.Parse("2026-09-02T18:51:04.800Z", CultureInfo.InvariantCulture);

    [Fact]
    public void Canonicalize_maps_real_judit_fixture_to_canonical_legal_case()
    {
        var rawContent = File.ReadAllText(FixturePath);
        var adapter = new JuditProcessSourceAdapter();
        var source = new ProcessSourceDocument(
            JuditProcessSourceAdapter.JuditSourceSystem,
            "Judit",
            "tests/fixtures/rj/response_60031603620268160021_1.json",
            rawContent,
            ObservedAt);

        var legalCase = adapter.Canonicalize(source);

        Assert.Equal("response_60031603620268160021_1", legalCase.Id.Value);
        Assert.Equal("6003160-36.2026.8.16.0021", legalCase.Cnj.Value);
        Assert.Equal("GISELE DE OLIVEIRA GALLI X CASIO BRASIL COMERCIO DE PRODUTOS ELETRONICOS LTDA", legalCase.Name);
        Assert.Equal("JUIZO DA 3º JUIZADO ESPECIAL CIVEL, CRIMINAL E DA FAZENDA PUBLICA DE CASCAVEL", RemoveAccents(legalCase.Court));
        Assert.Equal("INICIAL", legalCase.Phase);
        Assert.Equal("ATIVO", legalCase.Status);
        Assert.Equal(30000m, legalCase.Amount);
        Assert.Equal(4, legalCase.Parties.Count);
        Assert.Equal("***.271.359-**", legalCase.Parties[0].MainDocument);
        Assert.Equal("***.396.719-**", legalCase.Parties[1].MainDocument);
        Assert.Null(legalCase.Parties[2].MainDocument);
        Assert.Equal("**.172.255/0001-**", legalCase.Parties[3].MainDocument);
        Assert.Single(legalCase.Lawyers);
        Assert.Equal("PR0035553", legalCase.Lawyers[0].Oab);
        Assert.Equal(2, legalCase.Classifications.Count);
        Assert.Equal(3, legalCase.Subjects.Count);
        Assert.Equal(13, legalCase.Steps.Count);
        Assert.Equal("d5fb330f", legalCase.Steps[0].Id);
        Assert.Equal(2, legalCase.Attachments.Count);
        Assert.Equal("411788364428621657023616086781", legalCase.Attachments[0].Id);
        Assert.Contains(legalCase.Provenance, item => item.FieldPath == "cnj" && item.ObservedPath == "page_data[0].response_data.code");
        Assert.All(legalCase.Provenance, item => Assert.Equal("b5decf20e6bb330a7a58974711b7ec8e72215d74568e014e8cf1ced14ee916aa", item.SourceSha256));
    }

    [Fact]
    public void Canonicalization_service_routes_judit_source_system_to_judit_adapter()
    {
        var rawContent = File.ReadAllText(FixturePath);
        var service = new ProcessSourceCanonicalizationService(new[] { new JuditProcessSourceAdapter() });
        var source = new ProcessSourceDocument(
            "JUDIT",
            "Judit",
            "tests/fixtures/rj/response_60031603620268160021_1.json",
            rawContent,
            ObservedAt);

        var legalCase = service.Canonicalize(source);

        Assert.Equal("6003160-36.2026.8.16.0021", legalCase.Cnj.Value);
    }

    [Theory]
    [InlineData("02727135971", "***.271.359-**")]
    [InlineData("027.271.359-71", "***.271.359-**")]
    [InlineData("10172255000195", "**.172.255/0001-**")]
    [InlineData("10.172.255/0001-95", "**.172.255/0001-**")]
    public void Brazilian_document_masker_masks_cpf_and_cnpj(string input, string expected)
    {
        var masked = BrazilianDocumentMasker.MaskCpfCnpj(input);

        Assert.Equal(expected, masked);
    }

    [Fact]
    public void Canonicalize_preserves_raw_source_and_masks_only_canonical_party_documents()
    {
        var rawContent = File.ReadAllText(FixturePath);
        var source = new ProcessSourceDocument(
            JuditProcessSourceAdapter.JuditSourceSystem,
            "Judit",
            "tests/fixtures/rj/response_60031603620268160021_1.json",
            rawContent,
            ObservedAt);

        var legalCase = new JuditProcessSourceAdapter().Canonicalize(source);

        Assert.Contains("02727135971", source.RawContent, StringComparison.Ordinal);
        Assert.DoesNotContain("02727135971", legalCase.Parties.Select(party => party.MainDocument));
        Assert.Equal("PR0035553", legalCase.Lawyers[0].Oab);
    }

    private static string RemoveAccents(string value) =>
        string.Concat(value.Normalize(NormalizationForm.FormD).Where(character =>
            System.Globalization.CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark));

    private static string FindRepoRoot()
    {
        var current = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(current, "RJ.slnx")))
        {
            var parent = Directory.GetParent(current) ?? throw new InvalidOperationException("Repository root not found.");
            current = parent.FullName;
        }

        return current;
    }
}
