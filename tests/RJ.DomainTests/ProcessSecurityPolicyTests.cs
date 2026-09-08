using System.Globalization;
using RJ.Application.Security;
using RJ.Domain.Cases;

namespace RJ.DomainTests;

public sealed class ProcessSecurityPolicyTests
{
    private static readonly DateTimeOffset ObservedAt =
        DateTimeOffset.Parse("2026-09-02T18:51:04.800Z", CultureInfo.InvariantCulture);

    [Fact]
    public void Authorize_process_summary_denies_unlisted_case()
    {
        var caller = new CallerContext("tenant-1", "subject-1", ["other-case"], false);

        var decision = ProcessSecurityPolicy.AuthorizeProcessSummary(caller, Case());

        Assert.False(decision.IsAllowed);
        Assert.Equal("Caller is not authorized for this legal case.", decision.DenialReason);
    }

    [Fact]
    public void Authorize_process_summary_denies_sealed_case_without_permission()
    {
        var caller = new CallerContext("tenant-1", "subject-1", ["case-001"], false);

        var decision = ProcessSecurityPolicy.AuthorizeProcessSummary(caller, Case(sealedCase: true));

        Assert.False(decision.IsAllowed);
        Assert.Equal("Caller is not authorized for sealed legal cases.", decision.DenialReason);
    }

    [Fact]
    public void Authorize_process_summary_allows_authorized_unsealed_case()
    {
        var caller = new CallerContext("tenant-1", "subject-1", ["case-001"], false);

        var decision = ProcessSecurityPolicy.AuthorizeProcessSummary(caller, Case());

        Assert.True(decision.IsAllowed);
        Assert.Null(decision.DenialReason);
    }

    [Fact]
    public void Authorize_process_summary_denies_unauthorized_evidence_source()
    {
        var caller = new CallerContext("tenant-1", "subject-1", ["case-001"], false, ["DataJud"]);

        var decision = ProcessSecurityPolicy.AuthorizeProcessSummary(caller, Case());

        Assert.False(decision.IsAllowed);
        Assert.Equal("Caller is not authorized for one or more process evidence sources.", decision.DenialReason);
    }

    [Fact]
    public void Authorize_process_summary_allows_authorized_evidence_source()
    {
        var caller = new CallerContext("tenant-1", "subject-1", ["case-001"], false, ["source"]);

        var decision = ProcessSecurityPolicy.AuthorizeProcessSummary(caller, Case());

        Assert.True(decision.IsAllowed);
        Assert.Null(decision.DenialReason);
    }

    private static LegalCase Case(bool sealedCase = false) =>
        new(
            new LegalCaseId("case-001"),
            new LegalCaseCnj("6003160-36.2026.8.16.0021"),
            "GISELE DE OLIVEIRA GALLI X CASIO BRASIL COMERCIO DE PRODUTOS ELETRONICOS LTDA",
            "CASCAVEL - VARA DA FAZENDA PUBLICA",
            "INICIAL",
            "ATIVO",
            30000m,
            new[] { new LegalCaseParty("GISELE DE OLIVEIRA GALLI", "Active", "AUTOR", "***.271.359-**") },
            Array.Empty<LegalCaseLawyer>(),
            new[] { new LegalCaseClassification("436", "PROCEDIMENTO DO JUIZADO ESPECIAL CIVEL") },
            new[] { new LegalCaseSubject("899", "DIREITO CIVIL") },
            new[] { new LegalCaseStep("66b03cfa", ObservedAt, "1 - DISTRIBUIDO POR SORTEIO (CAS17VJ01)", "source") },
            Array.Empty<LegalCaseAttachment>(),
            new[]
            {
                new LegalCaseFieldProvenance(
                    sealedCase ? "secrecy_level" : "cnj",
                    "source",
                    "fixture.json",
                    "b5decf20e6bb330a7a58974711b7ec8e72215d74568e014e8cf1ced14ee916aa",
                    "page_data[0].response_data.secrecy_level",
                    ObservedAt)
            });
}
