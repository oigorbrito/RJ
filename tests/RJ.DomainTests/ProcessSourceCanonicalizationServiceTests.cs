using System.Globalization;
using RJ.Application.Sources;
using RJ.Domain.Cases;

namespace RJ.DomainTests;

public sealed class ProcessSourceCanonicalizationServiceTests
{
    private static readonly DateTimeOffset ObservedAt =
        DateTimeOffset.Parse("2026-09-02T18:51:04.800Z", CultureInfo.InvariantCulture);

    [Fact]
    public void Canonicalize_routes_neutral_source_document_to_registered_adapter()
    {
        var adapter = new CapturingAdapter("fixture-source");
        var service = new ProcessSourceCanonicalizationService(new[] { adapter });
        var source = new ProcessSourceDocument(
            " fixture-source ",
            " JEproc - TJPR - PR - Lawsuit - Auth - 1 instance ",
            " tests/fixtures/rj/response_60031603620268160021_1.json ",
            "{ \"response_type\": \"lawsuit\" }",
            ObservedAt);

        var legalCase = service.Canonicalize(source);

        Assert.Same(source, adapter.Captured);
        Assert.Equal("fixture-source", source.SourceSystem);
        Assert.Equal("JEproc - TJPR - PR - Lawsuit - Auth - 1 instance", source.SourceName);
        Assert.Equal("tests/fixtures/rj/response_60031603620268160021_1.json", source.SourceReference);
        Assert.Equal("6003160-36.2026.8.16.0021", legalCase.Cnj.Value);
    }

    [Fact]
    public void Canonicalize_rejects_unregistered_source_systems()
    {
        var service = new ProcessSourceCanonicalizationService(new[] { new CapturingAdapter("fixture-source") });
        var source = new ProcessSourceDocument(
            "other-source",
            "source",
            "fixture.json",
            "{}",
            ObservedAt);

        Assert.Throws<InvalidOperationException>(() => service.Canonicalize(source));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Source_document_rejects_blank_boundary_values(string value)
    {
        Assert.Throws<ArgumentException>(() => new ProcessSourceDocument(value, "source", "fixture.json", "{}", ObservedAt));
        Assert.Throws<ArgumentException>(() => new ProcessSourceDocument("fixture-source", value, "fixture.json", "{}", ObservedAt));
        Assert.Throws<ArgumentException>(() => new ProcessSourceDocument("fixture-source", "source", value, "{}", ObservedAt));
        Assert.Throws<ArgumentException>(() => new ProcessSourceDocument("fixture-source", "source", "fixture.json", value, ObservedAt));
    }

    private sealed class CapturingAdapter(string sourceSystem) : IProcessSourceAdapter
    {
        public string SourceSystem { get; } = sourceSystem;

        public ProcessSourceDocument? Captured { get; private set; }

        public LegalCase Canonicalize(ProcessSourceDocument source)
        {
            Captured = source;
            return new LegalCase(
                new LegalCaseId("response_60031603620268160021_1"),
                new LegalCaseCnj("6003160-36.2026.8.16.0021"),
                "GISELE DE OLIVEIRA GALLI X CASIO BRASIL COMERCIO DE PRODUTOS ELETRONICOS LTDA",
                "CASCAVEL - VARA DA FAZENDA PUBLICA",
                "INICIAL",
                "ATIVO",
                30000m,
                new[] { new LegalCaseParty("GISELE DE OLIVEIRA GALLI", "Active", "AUTOR", "02727135971") },
                Array.Empty<LegalCaseLawyer>(),
                new[] { new LegalCaseClassification("436", "PROCEDIMENTO DO JUIZADO ESPECIAL CIVEL") },
                new[] { new LegalCaseSubject("899", "DIREITO CIVIL") },
                new[] { new LegalCaseStep("66b03cfa", ObservedAt, "1 - DISTRIBUIDO POR SORTEIO (CAS17VJ01)", source.SourceName) },
                Array.Empty<LegalCaseAttachment>(),
                new[]
                {
                    new LegalCaseFieldProvenance(
                        "cnj",
                        source.SourceName,
                        source.SourceReference,
                        "b5decf20e6bb330a7a58974711b7ec8e72215d74568e014e8cf1ced14ee916aa",
                        "page_data[0].response_data.code",
                        source.ObservedAt)
                });
        }
    }
}
