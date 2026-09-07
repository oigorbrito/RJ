using System.Globalization;
using RJ.Application.Generation;
using RJ.Application.Sources;
using RJ.Domain.Cases;

namespace RJ.DomainTests;

public sealed class ProcessSummaryValidatorTests
{
    private static readonly DateTimeOffset ObservedAt =
        DateTimeOffset.Parse("2026-09-02T18:51:04.800Z", CultureInfo.InvariantCulture);

    [Fact]
    public void Validate_accepts_masked_deterministic_claims()
    {
        var legalCase = Case();
        var output = new GenerationModelOutput(
            false,
            null,
            [
                new GenerationClaim("CNJ: 6003160-36.2026.8.16.0021; documento: ***.271.359-**", []),
                new GenerationClaim("Nome: GISELE DE OLIVEIRA GALLI X CASIO BRASIL COMERCIO DE PRODUTOS ELETRONICOS LTDA", []),
                new GenerationClaim("Fase: INICIAL", []),
                new GenerationClaim("Status: ATIVO", []),
                new GenerationClaim("Parte: GISELE DE OLIVEIRA GALLI; polo: Active; tipo: AUTOR; documento: ***.271.359-**", []),
                new GenerationClaim("Movimentacao: 02/09/2026 15:51; id: 66b03cfa; 1 - DISTRIBUIDO POR SORTEIO (CAS17VJ01)", [])
            ]);

        var result = ProcessSummaryValidator.Validate(legalCase, output);

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Validate_rejects_raw_cpf_and_cnpj()
    {
        var legalCase = Case();
        var output = new GenerationModelOutput(
            false,
            null,
            [
                new GenerationClaim("CPF: 027.271.359-71", []),
                new GenerationClaim("CNPJ: 10.172.255/0001-95", [])
            ]);

        var result = ProcessSummaryValidator.Validate(legalCase, output);

        Assert.False(result.IsValid);
        Assert.Contains("Summary contains an unmasked CPF or CNPJ.", result.Errors);
    }

    [Fact]
    public void Validate_rejects_foreign_cnj_prognosis_and_attachment_content()
    {
        var legalCase = Case();
        var output = new GenerationModelOutput(
            false,
            null,
            [
                new GenerationClaim("CNJ relacionado 5003160-53.2026.8.16.0021", []),
                new GenerationClaim("A parte provavelmente deve ganhar.", []),
                new GenerationClaim("O anexo comprova o pedido inicial.", [])
            ]);

        var result = ProcessSummaryValidator.Validate(legalCase, output);

        Assert.False(result.IsValid);
        Assert.Contains("Summary contains a CNJ different from the canonical case CNJ.", result.Errors);
        Assert.Contains("Summary contains legal prognosis not supported by deterministic process evidence.", result.Errors);
        Assert.Contains("Summary claims attachment content even though attachment content is not observed.", result.Errors);
    }

    [Fact]
    public void Validate_rejects_missing_canonical_process_coverage()
    {
        var legalCase = Case();
        var output = new GenerationModelOutput(
            false,
            null,
            [new GenerationClaim("CNJ: 6003160-36.2026.8.16.0021", [])]);

        var result = ProcessSummaryValidator.Validate(legalCase, output);

        Assert.False(result.IsValid);
        Assert.Contains("Summary does not mention the canonical case name.", result.Errors);
        Assert.Contains("Summary does not mention the canonical phase.", result.Errors);
        Assert.Contains("Summary does not mention the canonical status.", result.Errors);
        Assert.Contains("Summary does not mention every canonical party name.", result.Errors);
        Assert.Contains("Summary movement claim count does not match the canonical inline movement count.", result.Errors);
        Assert.Contains("Summary does not mention every canonical movement date.", result.Errors);
    }

    [Fact]
    public void Validate_rejects_missing_deterministic_point_of_attention()
    {
        var legalCase = Case();
        var consistency = new LegalCaseConsistencyReport(
            legalCase.Cnj.Value,
            [
                new LegalCaseConsistencyFinding(
                    "status",
                    LegalCaseConsistencyStatus.Inconsistent,
                    [
                        new LegalCaseFieldObservation("status", "Judit", "judit.json", "status", "ATIVO"),
                        new LegalCaseFieldObservation("status", "DataJud", "datajud.json", "status", "BAIXADO")
                    ])
            ]);
        var output = new GenerationModelOutput(
            false,
            null,
            [
                new GenerationClaim("CNJ: 6003160-36.2026.8.16.0021; documento: ***.271.359-**", []),
                new GenerationClaim("Nome: GISELE DE OLIVEIRA GALLI X CASIO BRASIL COMERCIO DE PRODUTOS ELETRONICOS LTDA", []),
                new GenerationClaim("Fase: INICIAL", []),
                new GenerationClaim("Status: ATIVO", []),
                new GenerationClaim("Parte: GISELE DE OLIVEIRA GALLI; polo: Active; tipo: AUTOR; documento: ***.271.359-**", []),
                new GenerationClaim("Movimentacao: 02/09/2026 15:51; id: 66b03cfa; 1 - DISTRIBUIDO POR SORTEIO (CAS17VJ01)", [])
            ]);

        var result = ProcessSummaryValidator.Validate(legalCase, output, consistency);

        Assert.False(result.IsValid);
        Assert.Contains("Summary does not mention every deterministic point of attention.", result.Errors);
    }

    private static LegalCase Case() =>
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
                    "cnj",
                    "source",
                    "fixture.json",
                    "b5decf20e6bb330a7a58974711b7ec8e72215d74568e014e8cf1ced14ee916aa",
                    "page_data[0].response_data.code",
                    ObservedAt)
            });
}
