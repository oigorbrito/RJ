using RJ.Application.Benchmarking;
using RJ.Application.Evaluation;
using RJ.Application.Generation;
using RJ.Application.Retrieval;

namespace RJ.DomainTests;

public sealed class GenerationBenchmarkRunnerTests
{
    [Fact]
    public async Task RunAsync_orders_cases_and_passes_only_when_every_hard_gate_passes()
    {
        var caseA = CreateCase("case-a", "claim-a");
        var caseB = CreateCase("case-b", "claim-b");
        var model = new ContextAwareModel(context => ValidOutputFor(context));
        var runner = new GenerationBenchmarkRunner(model, new GenerationEvaluator());
        var catalog = new GenerationBenchmarkCatalog("catalog-1", [caseB, caseA]);

        var report = await runner.RunAsync(catalog, Metadata("catalog-1"), CancellationToken.None);

        Assert.True(report.Passed);
        Assert.Equal(2, report.PassedCases);
        Assert.Equal(["case-a", "case-b"], report.Cases.Select(item => item.CaseId).ToArray());
        Assert.Equal(1.0, report.MinimumClaimRecall);
        Assert.Equal(1.0, report.MinimumCitationValidity);
        Assert.Equal(1.0, report.MinimumGroundedness);
    }

    [Fact]
    public async Task RunAsync_does_not_average_away_a_failed_hard_gate()
    {
        var valid = CreateCase("case-a", "claim-a");
        var invalid = CreateCase("case-b", "claim-b");
        var model = new ContextAwareModel(context =>
        {
            if (context.CaseId == "case-b")
            {
                return new GenerationModelOutput(
                    false,
                    null,
                    [new GenerationClaim("claim-b", [new GenerationCitation("doc-x", new string('f', 64), 0, 5)])]);
            }

            return ValidOutputFor(context);
        });
        var runner = new GenerationBenchmarkRunner(model, new GenerationEvaluator());

        var report = await runner.RunAsync(
            new GenerationBenchmarkCatalog("catalog-1", [valid, invalid]),
            Metadata("catalog-1"),
            CancellationToken.None);

        Assert.False(report.Passed);
        Assert.Equal(1, report.PassedCases);
        Assert.Equal(1, report.FailedCases);
        Assert.True(report.MinimumCitationValidity < 1.0);
        Assert.True(report.MinimumGroundedness < 1.0);
    }

    [Fact]
    public async Task RunAsync_records_sanitized_case_exception_and_continues_batch()
    {
        const string secret = "provider payload /sensitive/path token=secret";
        var failing = CreateCase("case-a", "claim-a");
        var passing = CreateCase("case-b", "claim-b");
        var model = new ContextAwareModel(context =>
            context.CaseId == "case-a"
                ? throw new InvalidOperationException(secret)
                : ValidOutputFor(context));
        var runner = new GenerationBenchmarkRunner(model, new GenerationEvaluator());

        var report = await runner.RunAsync(
            new GenerationBenchmarkCatalog("catalog-1", [failing, passing]),
            Metadata("catalog-1"),
            CancellationToken.None);

        Assert.False(report.Passed);
        Assert.Equal(2, report.TotalCases);
        Assert.Equal("System.InvalidOperationException", report.Cases[0].ErrorType);
        Assert.Equal("Candidate execution failed.", report.Cases[0].ErrorMessage);
        Assert.DoesNotContain(secret, report.Cases[0].ErrorMessage, StringComparison.Ordinal);
        Assert.True(report.Cases[1].Passed);

        var json = GenerationBenchmarkJson.Serialize(report);
        Assert.Contains("\"errorType\": \"System.InvalidOperationException\"", json, StringComparison.Ordinal);
        Assert.Contains("\"errorMessage\": \"Candidate execution failed.\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain(secret, json, StringComparison.Ordinal);
        Assert.DoesNotContain("/sensitive/path", json, StringComparison.Ordinal);
        Assert.DoesNotContain("token=secret", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_rejects_catalog_version_mismatch_before_model_execution()
    {
        var model = new ContextAwareModel(ValidOutputFor);
        var runner = new GenerationBenchmarkRunner(model, new GenerationEvaluator());

        await Assert.ThrowsAsync<InvalidOperationException>(() => runner.RunAsync(
            new GenerationBenchmarkCatalog("catalog-1", [CreateCase("case-a", "claim-a")]),
            Metadata("catalog-2"),
            CancellationToken.None));

        Assert.Equal(0, model.CallCount);
    }

    [Fact]
    public async Task RunAsync_rejects_duplicate_fixture_ids_before_model_execution()
    {
        var model = new ContextAwareModel(ValidOutputFor);
        var runner = new GenerationBenchmarkRunner(model, new GenerationEvaluator());

        await Assert.ThrowsAsync<InvalidOperationException>(() => runner.RunAsync(
            new GenerationBenchmarkCatalog(
                "catalog-1",
                [CreateCase("case-a", "claim-a"), CreateCase("case-a", "claim-b")]),
            Metadata("catalog-1"),
            CancellationToken.None));

        Assert.Equal(0, model.CallCount);
    }

    [Fact]
    public async Task Json_report_contains_reproducibility_metadata_and_case_results()
    {
        var runner = new GenerationBenchmarkRunner(new ContextAwareModel(ValidOutputFor), new GenerationEvaluator());
        var report = await runner.RunAsync(
            new GenerationBenchmarkCatalog("catalog-1", [CreateCase("case-a", "claim-a")]),
            Metadata("catalog-1"),
            CancellationToken.None);

        var json = GenerationBenchmarkJson.Serialize(report);

        Assert.Contains("\"gitCommit\": \"abc123\"", json, StringComparison.Ordinal);
        Assert.Contains("\"modelId\": \"candidate-1\"", json, StringComparison.Ordinal);
        Assert.Contains("\"caseId\": \"case-a\"", json, StringComparison.Ordinal);
        Assert.Contains("\"passed\": true", json, StringComparison.Ordinal);
    }

    private static GenerationEvaluationCase CreateCase(string id, string claimText)
    {
        var excerpt = $"evidence-{id}";
        var position = SourcePosition.Create(0, excerpt.Length, excerpt.Length);
        var citation = new GenerationCitation("doc-1", new string('a', 64), 0, excerpt.Length);
        var context = new GenerationContext(
            id,
            claimText,
            1000,
            excerpt.Length,
            [new GenerationContextItem(id, "doc-1", "source.txt", new string('a', 64), excerpt, position, 1.0f)]);

        return new GenerationEvaluationCase(
            id,
            context,
            [new ExpectedGenerationClaim(claimText, [citation])],
            false);
    }

    private static GenerationModelOutput ValidOutputFor(GenerationContext context)
    {
        var item = Assert.Single(context.Items);
        return new GenerationModelOutput(
            false,
            null,
            [new GenerationClaim(
                context.Query,
                [new GenerationCitation(item.DocumentId, item.ContentSha256, item.Position.StartOffset, item.Position.Length)])]);
    }

    private static GenerationBenchmarkMetadata Metadata(string catalogVersion) => new(
        "abc123",
        ".NET 10.0.0",
        catalogVersion,
        "candidate-1",
        "temperature=0",
        "42");

    private sealed class ContextAwareModel(Func<GenerationContext, GenerationModelOutput> outputFactory) : IGenerationModel
    {
        public int CallCount { get; private set; }

        public Task<GenerationModelOutput> GenerateAsync(GenerationContext context, CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(outputFactory(context));
        }
    }
}
