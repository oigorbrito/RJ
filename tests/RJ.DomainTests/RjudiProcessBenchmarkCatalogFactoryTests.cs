using System.Globalization;
using RJ.Application.Benchmarking;
using RJ.Application.Evaluation;
using RJ.Application.Generation;
using RJ.Application.Sources;

namespace RJ.DomainTests;

public sealed class RjudiProcessBenchmarkCatalogFactoryTests
{
    private static readonly string RepoRoot = FindRepoRoot();
    private static readonly string FixturePath = Path.Combine(RepoRoot, "tests", "fixtures", "rj", "response_60031603620268160021_1.json");
    private static readonly DateTimeOffset ObservedAt =
        DateTimeOffset.Parse("2026-09-02T18:51:04.800Z", CultureInfo.InvariantCulture);

    [Fact]
    public async Task CreateAsync_builds_catalog_that_runs_through_existing_generation_benchmark_runner()
    {
        var legalCase = LoadCanonicalCase();
        var factory = new RjudiProcessBenchmarkCatalogFactory(
            new ProcessGenerationContextComposer(new GenerationContextBuilder()),
            new DeterministicProcessSummaryModel());
        var catalog = await factory.CreateAsync(
            legalCase,
            [],
            null,
            "Resuma o processo.",
            CancellationToken.None);
        var metadata = new GenerationBenchmarkMetadata(
            "c627a1bcdc87ff9b0bbd5ccc0b7d108daa5e324d",
            ".NET 10 local",
            RjudiProcessBenchmarkCatalogFactory.CatalogVersion,
            "deterministic-process-summary-v1",
            "prompt=rjudi-process-summary-v1; retrieval_calls=0",
            "none");
        var runner = new GenerationBenchmarkRunner(new DeterministicProcessSummaryModel(), new GenerationEvaluator());

        var report = await runner.RunAsync(catalog, metadata, CancellationToken.None);

        Assert.True(report.Passed);
        Assert.Equal(1, report.TotalCases);
        Assert.Equal(1.0, report.MinimumClaimRecall);
        Assert.Equal(1.0, report.MinimumCitationValidity);
        Assert.Equal(1.0, report.MinimumGroundedness);
        var evaluationCase = Assert.Single(catalog.Cases);
        Assert.Equal(RjudiProcessBenchmarkCatalogFactory.CaseId, evaluationCase.Id);
        Assert.Contains("retrieval_calls=0", metadata.ModelConfiguration, StringComparison.Ordinal);
        Assert.Equal(13, evaluationCase.Context.Items.Count(item => item.Excerpt.StartsWith("Movimentacao:", StringComparison.Ordinal)));
        Assert.DoesNotContain(evaluationCase.Context.Items, item => item.Excerpt.Contains("02727135971", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CreateAsync_retains_consistency_inconsistencies_in_expected_claims()
    {
        var legalCase = LoadCanonicalCase();
        var dataJudCase = legalCase.WithStatusFrom("datajud-case", "DataJud", "BAIXADO");
        var consistency = LegalCaseConsistencyEngine.Analyze([legalCase, dataJudCase]);
        var factory = new RjudiProcessBenchmarkCatalogFactory(
            new ProcessGenerationContextComposer(new GenerationContextBuilder()),
            new DeterministicProcessSummaryModel());

        var catalog = await factory.CreateAsync(
            legalCase,
            [],
            consistency,
            "Resuma o processo.",
            CancellationToken.None);

        var evaluationCase = Assert.Single(catalog.Cases);
        Assert.Contains(evaluationCase.ExpectedClaims, claim => claim.Text.StartsWith("Inconsistencia: campo: status", StringComparison.Ordinal));
    }

    private static RJ.Domain.Cases.LegalCase LoadCanonicalCase()
    {
        var rawContent = File.ReadAllText(FixturePath);
        var source = new ProcessSourceDocument(
            JuditProcessSourceAdapter.JuditSourceSystem,
            "Judit",
            "tests/fixtures/rj/response_60031603620268160021_1.json",
            rawContent,
            ObservedAt);

        return new JuditProcessSourceAdapter().Canonicalize(source);
    }

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

internal static class LegalCaseBenchmarkTestExtensions
{
    public static RJ.Domain.Cases.LegalCase WithStatusFrom(
        this RJ.Domain.Cases.LegalCase legalCase,
        string id,
        string sourceName,
        string status) =>
        new(
            new RJ.Domain.Cases.LegalCaseId(id),
            legalCase.Cnj,
            legalCase.Name,
            legalCase.Court,
            legalCase.Phase,
            status,
            legalCase.Amount,
            legalCase.Parties,
            legalCase.Lawyers,
            legalCase.Classifications,
            legalCase.Subjects,
            legalCase.Steps,
            legalCase.Attachments,
            new[]
            {
                new RJ.Domain.Cases.LegalCaseFieldProvenance(
                    "status",
                    sourceName,
                    $"{id}.json",
                    "b5decf20e6bb330a7a58974711b7ec8e72215d74568e014e8cf1ced14ee916aa",
                    "status",
                    DateTimeOffset.Parse("2026-09-02T18:51:04.800Z", CultureInfo.InvariantCulture))
            });
}
