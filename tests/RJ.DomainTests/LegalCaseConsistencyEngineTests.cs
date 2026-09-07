using System.Globalization;
using RJ.Application.Sources;
using RJ.Domain.Cases;

namespace RJ.DomainTests;

public sealed class LegalCaseConsistencyEngineTests
{
    private static readonly string[] ExpectedStatusValues = ["ATIVO", "BAIXADO"];
    private static readonly string[] ExpectedStatusSources = ["Judit", "DataJud"];

    private static readonly DateTimeOffset ObservedAt =
        DateTimeOffset.Parse("2026-09-02T18:51:04.800Z", CultureInfo.InvariantCulture);

    [Fact]
    public void Analyze_reports_deterministic_field_inconsistencies_without_merging_values()
    {
        var judit = Case("judit-id", "Judit", "ATIVO", "INICIAL", "page_data[0].response_data.status");
        var dataJud = Case("datajud-id", "DataJud", "BAIXADO", "INICIAL", "items[0].status");

        var first = LegalCaseConsistencyEngine.Analyze(new[] { judit, dataJud });
        var second = LegalCaseConsistencyEngine.Analyze(new[] { dataJud, judit });

        Assert.Equal("6003160-36.2026.8.16.0021", first.Cnj);
        Assert.Equal(first.Findings.Select(item => item.FieldPath), second.Findings.Select(item => item.FieldPath));
        Assert.Equal(first.Inconsistencies.Select(item => item.FieldPath), second.Inconsistencies.Select(item => item.FieldPath));

        var status = Assert.Single(first.Inconsistencies, item => item.FieldPath == "status");
        Assert.Equal(LegalCaseConsistencyStatus.Inconsistent, status.Status);
        Assert.Equal(ExpectedStatusValues, status.Observations.Select(item => item.Value));
        Assert.Equal(ExpectedStatusSources, status.Observations.Select(item => item.SourceName));

        Assert.Contains(first.Findings, item => item.FieldPath == "phase" && item.Status == LegalCaseConsistencyStatus.Consistent);
    }

    [Fact]
    public void Analyze_rejects_different_cnj_values()
    {
        var first = Case("first", "Judit", "ATIVO", "INICIAL", "page_data[0].response_data.status");
        var second = Case("second", "DataJud", "ATIVO", "INICIAL", "items[0].status", "5003160-53.2026.8.16.0021");

        Assert.Throws<InvalidOperationException>(() => LegalCaseConsistencyEngine.Analyze(new[] { first, second }));
    }

    private static LegalCase Case(
        string id,
        string sourceName,
        string status,
        string phase,
        string observedPath,
        string cnj = "6003160-36.2026.8.16.0021") =>
        new(
            new LegalCaseId(id),
            new LegalCaseCnj(cnj),
            "GISELE DE OLIVEIRA GALLI X CASIO BRASIL COMERCIO DE PRODUTOS ELETRONICOS LTDA",
            "CASCAVEL - VARA DA FAZENDA PUBLICA",
            phase,
            status,
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
                    ObservedAt),
                new LegalCaseFieldProvenance(
                    "phase",
                    sourceName,
                    $"{id}.json",
                    "b5decf20e6bb330a7a58974711b7ec8e72215d74568e014e8cf1ced14ee916aa",
                    "phase",
                    ObservedAt)
            });
}
