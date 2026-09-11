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
                Claim("CNJ: 6003160-36.2026.8.16.0021; documento: ***.271.359-**"),
                Claim("Nome: GISELE DE OLIVEIRA GALLI X CASIO BRASIL COMERCIO DE PRODUTOS ELETRONICOS LTDA"),
                Claim("Juizo: CASCAVEL - VARA DA FAZENDA PUBLICA"),
                Claim("Fase: INICIAL"),
                Claim("Status: ATIVO"),
                Claim("Sigilo: 0"),
                Claim("Valor da causa: 30000"),
                Claim("Parte: GISELE DE OLIVEIRA GALLI; polo: Active; tipo: AUTOR; documento: ***.271.359-**"),
                Claim("Advogado: ANDREIA BELO ROSSO; OAB: PR0035553"),
                Claim("Classe: 436 - PROCEDIMENTO DO JUIZADO ESPECIAL CIVEL"),
                Claim("Assunto: 899 - DIREITO CIVIL"),
                Claim("Movimentacao: 02/09/2026 15:51; id: 66b03cfa; 1 - DISTRIBUIDO POR SORTEIO (CAS17VJ01)")
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
                Claim("CPF: 027.271.359-71"),
                Claim("CNPJ: 10.172.255/0001-95")
            ]);

        var result = ProcessSummaryValidator.Validate(legalCase, output);

        Assert.False(result.IsValid);
        Assert.Contains("Summary contains an unmasked CPF or CNPJ.", result.Errors);
    }

    [Fact]
    public void Validate_rejects_raw_cpf_and_cnpj_with_non_ascii_digits()
    {
        var legalCase = Case();
        var output = new GenerationModelOutput(
            false,
            null,
            [
                Claim("CPF: \uFF10\uFF12\uFF17\uFF12\uFF17\uFF11\uFF13\uFF15\uFF19\uFF17\uFF11"),
                Claim("CNPJ: \uFF11\uFF10\uFF11\uFF17\uFF12\uFF12\uFF15\uFF15\uFF10\uFF10\uFF10\uFF11\uFF19\uFF15")
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
                Claim("CNJ relacionado 5003160-53.2026.8.16.0021"),
                Claim("A parte provavelmente deve ganhar."),
                Claim("O anexo comprova o pedido inicial.")
            ]);

        var result = ProcessSummaryValidator.Validate(legalCase, output);

        Assert.False(result.IsValid);
        Assert.Contains("Summary contains a CNJ different from the canonical case CNJ.", result.Errors);
        Assert.Contains("Summary contains legal prognosis not supported by deterministic process evidence.", result.Errors);
        Assert.Contains("Summary claims attachment content even though attachment content is not observed.", result.Errors);
    }

    [Fact]
    public void Validate_rejects_foreign_cnj_with_non_ascii_digits()
    {
        var legalCase = Case();
        var output = new GenerationModelOutput(
            false,
            null,
            [Claim("CNJ relacionado \uFF15\uFF10\uFF10\uFF13\uFF11\uFF16\uFF10-\uFF15\uFF13.\uFF12\uFF10\uFF12\uFF16.\uFF18.\uFF11\uFF16.\uFF10\uFF10\uFF12\uFF11")]);

        var result = ProcessSummaryValidator.Validate(legalCase, output);

        Assert.False(result.IsValid);
        Assert.Contains("Summary contains a CNJ different from the canonical case CNJ.", result.Errors);
    }

    [Fact]
    public void Validate_rejects_abstention_when_canonical_process_evidence_is_available()
    {
        var legalCase = Case();
        var output = new GenerationModelOutput(true, "insufficient evidence", []);

        var result = ProcessSummaryValidator.Validate(legalCase, output);

        Assert.False(result.IsValid);
        Assert.Contains("Process summary abstained despite available canonical process evidence.", result.Errors);
    }

    [Fact]
    public void Validate_rejects_uncited_factual_claims()
    {
        var legalCase = Case();
        var output = new GenerationModelOutput(
            false,
            null,
            [
                new GenerationClaim("CNJ: 6003160-36.2026.8.16.0021; documento: ***.271.359-**", []),
                Claim("Nome: GISELE DE OLIVEIRA GALLI X CASIO BRASIL COMERCIO DE PRODUTOS ELETRONICOS LTDA"),
                Claim("Juizo: CASCAVEL - VARA DA FAZENDA PUBLICA"),
                Claim("Fase: INICIAL"),
                Claim("Status: ATIVO"),
                Claim("Sigilo: 0"),
                Claim("Valor da causa: 30000"),
                Claim("Parte: GISELE DE OLIVEIRA GALLI; polo: Active; tipo: AUTOR; documento: ***.271.359-**"),
                Claim("Advogado: ANDREIA BELO ROSSO; OAB: PR0035553"),
                Claim("Classe: 436 - PROCEDIMENTO DO JUIZADO ESPECIAL CIVEL"),
                Claim("Assunto: 899 - DIREITO CIVIL"),
                Claim("Movimentacao: 02/09/2026 15:51; id: 66b03cfa; 1 - DISTRIBUIDO POR SORTEIO (CAS17VJ01)")
            ]);

        var result = ProcessSummaryValidator.Validate(legalCase, output);

        Assert.False(result.IsValid);
        Assert.Contains("Summary contains an uncited factual claim.", result.Errors);
    }

    [Fact]
    public void Validate_rejects_missing_canonical_process_coverage()
    {
        var legalCase = Case();
        var output = new GenerationModelOutput(
            false,
            null,
            [Claim("CNJ: 6003160-36.2026.8.16.0021")]);

        var result = ProcessSummaryValidator.Validate(legalCase, output);

        Assert.False(result.IsValid);
        Assert.Contains("Summary does not mention the canonical case name.", result.Errors);
        Assert.Contains("Summary does not mention the canonical court.", result.Errors);
        Assert.Contains("Summary does not mention the canonical phase.", result.Errors);
        Assert.Contains("Summary does not mention the canonical status.", result.Errors);
        Assert.Contains("Summary does not mention the canonical secrecy level.", result.Errors);
        Assert.Contains("Summary does not mention every canonical party name.", result.Errors);
        Assert.Contains("Summary does not mention the canonical amount.", result.Errors);
        Assert.Contains("Summary does not mention every canonical lawyer.", result.Errors);
        Assert.Contains("Summary does not mention every canonical classification.", result.Errors);
        Assert.Contains("Summary does not mention every canonical subject.", result.Errors);
        Assert.Contains("Summary movement claim count does not match the canonical inline movement count.", result.Errors);
        Assert.Contains("Summary does not mention every canonical movement date.", result.Errors);
    }

    [Fact]
    public void Validate_rejects_missing_required_classification_and_subject_sections()
    {
        var legalCase = Case();
        var output = new GenerationModelOutput(
            false,
            null,
            [
                Claim("CNJ: 6003160-36.2026.8.16.0021; documento: ***.271.359-**"),
                Claim("Nome: GISELE DE OLIVEIRA GALLI X CASIO BRASIL COMERCIO DE PRODUTOS ELETRONICOS LTDA"),
                Claim("Juizo: CASCAVEL - VARA DA FAZENDA PUBLICA"),
                Claim("Fase: INICIAL"),
                Claim("Status: ATIVO"),
                Claim("Sigilo: 0"),
                Claim("Valor da causa: 30000"),
                Claim("Parte: GISELE DE OLIVEIRA GALLI; polo: Active; tipo: AUTOR; documento: ***.271.359-**"),
                Claim("Advogado: ANDREIA BELO ROSSO; OAB: PR0035553"),
                Claim("Movimentacao: 02/09/2026 15:51; id: 66b03cfa; 1 - DISTRIBUIDO POR SORTEIO (CAS17VJ01)")
            ]);

        var result = ProcessSummaryValidator.Validate(legalCase, output);

        Assert.False(result.IsValid);
        Assert.Contains("Summary does not mention every canonical classification.", result.Errors);
        Assert.Contains("Summary does not mention every canonical subject.", result.Errors);
    }

    [Fact]
    public void Validate_rejects_missing_required_lawyer_section()
    {
        var legalCase = Case();
        var output = new GenerationModelOutput(
            false,
            null,
            [
                Claim("CNJ: 6003160-36.2026.8.16.0021; documento: ***.271.359-**"),
                Claim("Nome: GISELE DE OLIVEIRA GALLI X CASIO BRASIL COMERCIO DE PRODUTOS ELETRONICOS LTDA"),
                Claim("Juizo: CASCAVEL - VARA DA FAZENDA PUBLICA"),
                Claim("Fase: INICIAL"),
                Claim("Status: ATIVO"),
                Claim("Sigilo: 0"),
                Claim("Valor da causa: 30000"),
                Claim("Parte: GISELE DE OLIVEIRA GALLI; polo: Active; tipo: AUTOR; documento: ***.271.359-**"),
                Claim("Classe: 436 - PROCEDIMENTO DO JUIZADO ESPECIAL CIVEL"),
                Claim("Assunto: 899 - DIREITO CIVIL"),
                Claim("Movimentacao: 02/09/2026 15:51; id: 66b03cfa; 1 - DISTRIBUIDO POR SORTEIO (CAS17VJ01)")
            ]);

        var result = ProcessSummaryValidator.Validate(legalCase, output);

        Assert.False(result.IsValid);
        Assert.Contains("Summary does not mention every canonical lawyer.", result.Errors);
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
                Claim("CNJ: 6003160-36.2026.8.16.0021; documento: ***.271.359-**"),
                Claim("Nome: GISELE DE OLIVEIRA GALLI X CASIO BRASIL COMERCIO DE PRODUTOS ELETRONICOS LTDA"),
                Claim("Juizo: CASCAVEL - VARA DA FAZENDA PUBLICA"),
                Claim("Fase: INICIAL"),
                Claim("Status: ATIVO"),
                Claim("Sigilo: 0"),
                Claim("Valor da causa: 30000"),
                Claim("Parte: GISELE DE OLIVEIRA GALLI; polo: Active; tipo: AUTOR; documento: ***.271.359-**"),
                Claim("Advogado: ANDREIA BELO ROSSO; OAB: PR0035553"),
                Claim("Classe: 436 - PROCEDIMENTO DO JUIZADO ESPECIAL CIVEL"),
                Claim("Assunto: 899 - DIREITO CIVIL"),
                Claim("Movimentacao: 02/09/2026 15:51; id: 66b03cfa; 1 - DISTRIBUIDO POR SORTEIO (CAS17VJ01)")
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
            0,
            30000m,
            new[] { new LegalCaseParty("GISELE DE OLIVEIRA GALLI", "Active", "AUTOR", "***.271.359-**") },
            new[] { new LegalCaseLawyer("ANDREIA BELO ROSSO", "PR0035553") },
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

    private static GenerationClaim Claim(string text) =>
        new(text, [new GenerationCitation("doc-1", new string('a', 64), 0, text.Length)]);
}
