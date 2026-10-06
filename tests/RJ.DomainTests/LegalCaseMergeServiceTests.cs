using System.Globalization;
using RJ.Application.Sources;
using RJ.Domain.Cases;

namespace RJ.DomainTests;

public sealed class LegalCaseMergeServiceTests
{
    private static readonly string[] DataJudFirstPriority = ["DataJud", "Judit"];

    private static readonly DateTimeOffset ObservedAt =
        DateTimeOffset.Parse("2026-09-02T18:51:04.800Z", CultureInfo.InvariantCulture);

    [Fact]
    public void Merge_selects_primary_case_by_declared_source_priority_and_preserves_all_provenance()
    {
        var judit = Case("judit-id", "Judit", "ATIVO", "page_data[0].response_data.status");
        var dataJud = Case("datajud-id", "DataJud", "BAIXADO", "items[0].status");

        var merged = LegalCaseMergeService.Merge(new[] { judit, dataJud }, DataJudFirstPriority);

        Assert.Equal("datajud-id", merged.Id.Value);
        Assert.Equal("BAIXADO", merged.Status);
        Assert.Equal(2, merged.Provenance.Count);
        Assert.Equal("DataJud", merged.Provenance[0].SourceName);
        Assert.Equal("Judit", merged.Provenance[1].SourceName);
    }

    [Fact]
    public void Merge_is_deterministic_when_input_order_changes()
    {
        var judit = Case("judit-id", "Judit", "ATIVO", "page_data[0].response_data.status");
        var dataJud = Case("datajud-id", "DataJud", "BAIXADO", "items[0].status");

        var first = LegalCaseMergeService.Merge(new[] { judit, dataJud }, DataJudFirstPriority);
        var second = LegalCaseMergeService.Merge(new[] { dataJud, judit }, DataJudFirstPriority);

        Assert.Equal(first.Id.Value, second.Id.Value);
        Assert.Equal(first.Status, second.Status);
        Assert.Equal(first.Provenance.Select(item => item.SourceName), second.Provenance.Select(item => item.SourceName));
    }

    [Fact]
    public void Merge_rejects_different_cnj_values()
    {
        var first = Case("first", "Judit", "ATIVO", "page_data[0].response_data.status");
        var second = Case("second", "DataJud", "ATIVO", "items[0].status", "5003160-53.2026.8.16.0021");

        Assert.Throws<InvalidOperationException>(() => LegalCaseMergeService.Merge(new[] { first, second }, DataJudFirstPriority));
    }

    private static LegalCase Case(
        string id,
        string sourceName,
        string status,
        string observedPath,
        string cnj = "6003160-36.2026.8.16.0021") =>
        new(
            new LegalCaseId(id),
            new LegalCaseCnj(cnj),
            "GISELE DE OLIVEIRA GALLI X CASIO BRASIL COMERCIO DE PRODUTOS ELETRONICOS LTDA",
            "CASCAVEL - VARA DA FAZENDA PUBLICA",
            "INICIAL",
            status,
            0,
            30000m,
            new[] { new LegalCaseParty("GISELE DE OLIVEIRA GALLI", "Active", "AUTOR", "***.271.359-**") },
            Array.Empty<LegalCaseLawyer>(),
            new[] { new LegalCaseClassification("436", "PROCEDIMENTO DO JUIZADO ESPECIAL CIVEL") },
            new[] { new LegalCaseSubject("899", "DIREITO CIVIL") },
            new[] { new LegalCaseStep("66b03cfa", ObservedAt, "1 - DISTRIBUIDO POR SORTEIO (CAS17VJ01)", sourceName) },
            Array.Empty<LegalCaseAttachment>(),
            new[]
            {
                new LegalCaseFieldProvenance(
                    "status",
                    sourceName,
                    $"{id}.json",
                    "b5decf20e6bb330a7a58974711b7ec8e72215d74568e014e8cf1ced14ee916aa",
                    observedPath,
                    ObservedAt)
            });
}
