using System.Globalization;
using RJ.Application.Sources;

namespace RJ.DomainTests;

public sealed class DataJudProcessSourceAdapterTests
{
    private static readonly DateTimeOffset ObservedAt =
        DateTimeOffset.Parse("2026-09-07T12:00:00Z", CultureInfo.InvariantCulture);

    [Fact]
    public void Canonicalize_maps_datajud_hit_to_canonical_legal_case_without_inventing_absent_parties()
    {
        var rawContent = """
        {
          "hits": {
            "hits": [
              {
                "_source": {
                  "numeroProcesso": "60031603620268160021",
                  "classe": { "codigo": "436", "nome": "Procedimento do Juizado Especial Civel" },
                  "tribunal": "TJPR",
                  "grau": "G1",
                  "orgaoJulgador": { "nome": "3 Juizado Especial Civel de Cascavel" },
                  "assuntos": [
                    { "codigo": "14033", "nome": "Indenizacao por dano moral" },
                    { "codigo": "10431", "nome": "Responsabilidade civil" }
                  ],
                  "movimentos": [
                    { "codigo": "1", "nome": "Distribuido por sorteio", "dataHora": "2026-09-01T20:33:37Z" },
                    { "codigo": "4", "nome": "Audiencia de conciliacao designada", "dataHora": "2026-09-01T20:33:39Z" }
                  ]
                }
              }
            ]
          }
        }
        """;
        var source = new ProcessSourceDocument(
            DataJudProcessSourceAdapter.DataJudSourceSystem,
            "DataJud",
            "datajud/60031603620268160021.json",
            rawContent,
            ObservedAt);

        var legalCase = new DataJudProcessSourceAdapter().Canonicalize(source);

        Assert.Equal("6003160-36.2026.8.16.0021", legalCase.Cnj.Value);
        Assert.Equal("Processo 6003160-36.2026.8.16.0021", legalCase.Name);
        Assert.Equal("3 Juizado Especial Civel de Cascavel", legalCase.Court);
        Assert.Equal("G1", legalCase.Phase);
        Assert.Equal("NAO OBSERVADO", legalCase.Status);
        Assert.Null(legalCase.Amount);
        var party = Assert.Single(legalCase.Parties);
        Assert.Equal("NAO OBSERVADO", party.Name);
        Assert.Null(party.MainDocument);
        Assert.Empty(legalCase.Lawyers);
        Assert.Single(legalCase.Classifications);
        Assert.Equal(2, legalCase.Subjects.Count);
        Assert.Equal(2, legalCase.Steps.Count);
        Assert.Equal("Distribuido por sorteio", legalCase.Steps[0].Content);
        Assert.Empty(legalCase.Attachments);
        Assert.Contains(legalCase.Provenance, item => item.FieldPath == "parties" && item.ObservedPath == "NAO OBSERVADO");
        Assert.All(legalCase.Provenance, item => Assert.Equal(source.RawContentSha256(), item.SourceSha256));
    }

    [Fact]
    public void Canonicalization_service_routes_datajud_source_system_to_datajud_adapter()
    {
        var rawContent = """
        {
          "numeroProcesso": "60031603620268160021",
          "classe": { "codigo": "436", "nome": "Procedimento do Juizado Especial Civel" },
          "tribunal": "TJPR",
          "assuntos": [{ "codigo": "14033", "nome": "Indenizacao por dano moral" }],
          "movimentos": [{ "codigo": "1", "nome": "Distribuido por sorteio", "dataHora": "2026-09-01T20:33:37Z" }]
        }
        """;
        var service = new ProcessSourceCanonicalizationService(new IProcessSourceAdapter[]
        {
            new JuditProcessSourceAdapter(),
            new DataJudProcessSourceAdapter()
        });

        var legalCase = service.Canonicalize(new ProcessSourceDocument(
            "DATAJUD",
            "DataJud",
            "datajud/60031603620268160021.json",
            rawContent,
            ObservedAt));

        Assert.Equal("6003160-36.2026.8.16.0021", legalCase.Cnj.Value);
    }
}
