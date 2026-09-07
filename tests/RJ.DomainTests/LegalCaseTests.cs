using RJ.Domain.Cases;
using System.Globalization;

namespace RJ.DomainTests;

public sealed class LegalCaseTests
{
    [Fact]
    public void Constructor_preserves_canonical_process_fields()
    {
        var legalCase = BuildObservedFixtureCase();

        Assert.Equal("response_60031603620268160021_1", legalCase.Id.Value);
        Assert.Equal("6003160-36.2026.8.16.0021", legalCase.Cnj.Value);
        Assert.Equal("GISELE DE OLIVEIRA GALLI X CASIO BRASIL COMERCIO DE PRODUTOS ELETRONICOS LTDA", legalCase.Name);
        Assert.Equal("JUIZO DA 3 JUIZADO ESPECIAL CIVEL, CRIMINAL E DA FAZENDA PUBLICA DE CASCAVEL", legalCase.Court);
        Assert.Equal("INICIAL", legalCase.Phase);
        Assert.Equal("ATIVO", legalCase.Status);
        Assert.Equal(30000m, legalCase.Amount);
        Assert.Equal(4, legalCase.Parties.Count);
        Assert.Single(legalCase.Lawyers);
        Assert.Equal(2, legalCase.Classifications.Count);
        Assert.Equal(3, legalCase.Subjects.Count);
        Assert.Equal(13, legalCase.Steps.Count);
        Assert.Equal(2, legalCase.Attachments.Count);
        Assert.Contains(legalCase.Provenance, item => item.FieldPath == "cnj" && item.ObservedPath == "page_data[0].response_data.code");
        Assert.Contains(legalCase.Provenance, item => item.FieldPath == "steps" && item.ObservedPath == "page_data[0].response_data.steps");
        Assert.Equal("411788364428621657023616086781", legalCase.Attachments[0].Id);
    }

    [Fact]
    public void Constructor_rejects_missing_required_process_collections()
    {
        Assert.Throws<ArgumentException>(() => new LegalCase(
            new LegalCaseId("case-1"),
            new LegalCaseCnj("6003160-36.2026.8.16.0021"),
            "case name",
            "court",
            "phase",
            "status",
            null,
            Array.Empty<LegalCaseParty>(),
            Array.Empty<LegalCaseLawyer>(),
            new[] { new LegalCaseClassification("436", "classification") },
            new[] { new LegalCaseSubject("899", "subject") },
            new[] { new LegalCaseStep("step-1", DateTimeOffset.Parse("2026-09-01T20:33:37.000Z", CultureInfo.InvariantCulture), "content", "source") },
            Array.Empty<LegalCaseAttachment>(),
            new[] { Provenance("cnj", "page_data[0].response_data.code") }));
    }

    [Fact]
    public void Constructor_rejects_missing_process_provenance()
    {
        Assert.Throws<ArgumentException>(() => new LegalCase(
            new LegalCaseId("case-1"),
            new LegalCaseCnj("6003160-36.2026.8.16.0021"),
            "case name",
            "court",
            "phase",
            "status",
            null,
            new[] { new LegalCaseParty("party", "Active", "AUTOR", null) },
            Array.Empty<LegalCaseLawyer>(),
            new[] { new LegalCaseClassification("436", "classification") },
            new[] { new LegalCaseSubject("899", "subject") },
            new[] { new LegalCaseStep("step-1", DateTimeOffset.Parse("2026-09-01T20:33:37.000Z", CultureInfo.InvariantCulture), "content", "source") },
            Array.Empty<LegalCaseAttachment>(),
            Array.Empty<LegalCaseFieldProvenance>()));
    }

    [Fact]
    public void Provenance_normalizes_hash_and_rejects_invalid_hashes()
    {
        var provenance = new LegalCaseFieldProvenance(
            " cnj ",
            " source ",
            " fixture.json ",
            new string('A', 64),
            " page_data[0].response_data.code ",
            DateTimeOffset.Parse("2026-09-02T18:51:04.800Z", CultureInfo.InvariantCulture));

        Assert.Equal("cnj", provenance.FieldPath);
        Assert.Equal("source", provenance.SourceName);
        Assert.Equal("fixture.json", provenance.SourceReference);
        Assert.Equal(new string('a', 64), provenance.SourceSha256);
        Assert.Equal("page_data[0].response_data.code", provenance.ObservedPath);
        Assert.Throws<ArgumentException>(() => new LegalCaseFieldProvenance("cnj", "source", "fixture.json", "invalid", "path", provenance.ObservedAt));
    }

    [Fact]
    public void Value_objects_trim_required_text_fields()
    {
        var party = new LegalCaseParty(" party ", " Active ", " AUTOR ", " 02727135971 ");
        var lawyer = new LegalCaseLawyer(" lawyer ", " PR0035553 ");

        Assert.Equal("party", party.Name);
        Assert.Equal("Active", party.Side);
        Assert.Equal("AUTOR", party.PersonType);
        Assert.Equal("02727135971", party.MainDocument);
        Assert.Equal("lawyer", lawyer.Name);
        Assert.Equal("PR0035553", lawyer.Oab);
    }

    private static LegalCase BuildObservedFixtureCase() =>
        new(
            new LegalCaseId("response_60031603620268160021_1"),
            new LegalCaseCnj("6003160-36.2026.8.16.0021"),
            "GISELE DE OLIVEIRA GALLI X CASIO BRASIL COMERCIO DE PRODUTOS ELETRONICOS LTDA",
            "JUIZO DA 3 JUIZADO ESPECIAL CIVEL, CRIMINAL E DA FAZENDA PUBLICA DE CASCAVEL",
            "INICIAL",
            "ATIVO",
            30000m,
            new[]
            {
                new LegalCaseParty("GISELE DE OLIVEIRA GALLI", "Active", "AUTOR", "02727135971"),
                new LegalCaseParty("MATHEUS RIBEIRO GALLI", "Active", "AUTOR", "00439671914"),
                new LegalCaseParty("ANDREIA BELO ROSSO", "Active", "ADVOGADO", null),
                new LegalCaseParty("CASIO BRASIL COMERCIO DE PRODUTOS ELETRONICOS LTDA", "Passive", "REU", "10172255000195")
            },
            new[]
            {
                new LegalCaseLawyer("ANDREIA BELO ROSSO", "PR0035553")
            },
            new[]
            {
                new LegalCaseClassification("436", "PROCEDIMENTO DO JUIZADO ESPECIAL CIVEL"),
                new LegalCaseClassification("169", "EMBARGOS")
            },
            new[]
            {
                new LegalCaseSubject("14033", "INDENIZACAO POR DANO MORAL"),
                new LegalCaseSubject("10431", "RESPONSABILIDADE CIVIL"),
                new LegalCaseSubject("899", "DIREITO CIVIL")
            },
            Enumerable.Range(1, 13)
                .Select(number => new LegalCaseStep(
                    $"step-{number}",
                    DateTimeOffset.Parse("2026-09-01T20:33:37.000Z", CultureInfo.InvariantCulture),
                    $"movement {number}",
                    "JEproc - TJPR - PR - Lawsuit - Auth - 1 instance"))
                .ToArray(),
            new[]
            {
                new LegalCaseAttachment(
                    "411788364428621657023616086781",
                    "ATO ORDINATORIO 1 - SEM SIGILO",
                    "76960ae6",
                    "html",
                    "pending",
                    DateTimeOffset.Parse("2026-09-02T15:56:04.000Z", CultureInfo.InvariantCulture)),
                new LegalCaseAttachment(
                    "411788296402134084576493961260",
                    "ATO ORDINATORIO 1 - SEM SIGILO",
                    "bc3d4178",
                    "html",
                    "pending",
                    DateTimeOffset.Parse("2026-09-01T21:04:02.000Z", CultureInfo.InvariantCulture))
            },
            new[]
            {
                Provenance("cnj", "page_data[0].response_data.code"),
                Provenance("parties", "page_data[0].response_data.parties"),
                Provenance("classifications", "page_data[0].response_data.classifications"),
                Provenance("subjects", "page_data[0].response_data.subjects"),
                Provenance("steps", "page_data[0].response_data.steps"),
                Provenance("attachments", "page_data[0].response_data.attachments")
            });

    private static LegalCaseFieldProvenance Provenance(string fieldPath, string observedPath) =>
        new(
            fieldPath,
            "JEproc - TJPR - PR - Lawsuit - Auth - 1 instance",
            "tests/fixtures/rj/response_60031603620268160021_1.json",
            "b5decf20e6bb330a7a58974711b7ec8e72215d74568e014e8cf1ced14ee916aa",
            observedPath,
            DateTimeOffset.Parse("2026-09-02T18:51:04.800Z", CultureInfo.InvariantCulture));
}
