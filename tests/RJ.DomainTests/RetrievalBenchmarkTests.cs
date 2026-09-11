using RJ.Application.Benchmarking;
using RJ.Application.Retrieval;
using RJ.Domain.Cases;

namespace RJ.DomainTests;

public sealed class RetrievalBenchmarkTests
{
    [Fact]
    public async Task Runner_measures_first_exact_document_and_hash_match()
    {
        var expectedHash = new string('b', 64);
        var search = new FakeSearch(new Dictionary<string, IReadOnlyList<LegalDocumentSearchHit>>(StringComparer.Ordinal)
        {
            ["question"] =
            [
                Hit("case-1", "doc-1", new string('a', 64), 2.0f),
                Hit("case-1", "doc-2", expectedHash, 1.0f)
            ]
        });
        var report = await new RetrievalBenchmarkRunner(search).RunAsync(
            Catalog("doc-2", expectedHash),
            Treatment(),
            5,
            CancellationToken.None);

        var item = Assert.Single(report.Cases);
        Assert.True(item.ExecutionSucceeded);
        Assert.Equal(0, item.HitAt1);
        Assert.Equal(1, item.HitAt3);
        Assert.Equal(1, item.HitAt5);
        Assert.Equal(0.5, item.Mrr);
        Assert.Equal(2, Assert.Single(item.Queries).FirstRelevantRank);
    }

    [Fact]
    public async Task Runner_does_not_treat_same_text_as_relevant_without_exact_evidence_identity()
    {
        var search = new FakeSearch(new Dictionary<string, IReadOnlyList<LegalDocumentSearchHit>>(StringComparer.Ordinal)
        {
            ["question"] = [Hit("case-1", "different-doc", new string('c', 64), 1.0f, "same expected words")]
        });
        var report = await new RetrievalBenchmarkRunner(search).RunAsync(
            Catalog("expected-doc", new string('d', 64)),
            Treatment(),
            5,
            CancellationToken.None);

        var item = Assert.Single(report.Cases);
        Assert.Equal(0, item.HitAt5);
        Assert.Equal(0, item.Mrr);
        Assert.Null(Assert.Single(item.Queries).FirstRelevantRank);
    }

    [Fact]
    public async Task Runner_records_treatment_execution_failure_separately_from_quality()
    {
        var report = await new RetrievalBenchmarkRunner(new ThrowingSearch()).RunAsync(
            Catalog("doc-1", new string('a', 64)),
            Treatment(),
            5,
            CancellationToken.None);

        var item = Assert.Single(report.Cases);
        Assert.False(item.ExecutionSucceeded);
        Assert.Equal(0, item.QueryCount);
        Assert.Empty(item.Queries);
        Assert.NotNull(item.ErrorType);
        Assert.Contains(nameof(InvalidOperationException), item.ErrorType!, StringComparison.Ordinal);
    }

    [Fact]
    public void Materializer_preserves_case_quality_metrics_and_provenance()
    {
        var report = new RetrievalBenchmarkReport(
            RetrievalBenchmarkReport.SupportedFormatVersion,
            "catalog-v1",
            Treatment(),
            [new RetrievalBenchmarkCaseReport(
                "case-1",
                4,
                1,
                2,
                3,
                0.625,
                12.5,
                [
                    new("q1", 1, true, true, true),
                    new("q2", 2, false, true, true),
                    new("q3", 5, false, false, true),
                    new("q4", null, false, false, false)
                ],
                null,
                null)]).Validate();

        var item = Assert.Single(new RetrievalEmpiricalObservationMaterializer().Materialize(
            report,
            "reports/r0.json",
            new string('e', 64),
            "policies/r0.json",
            new string('f', 64),
            DateTimeOffset.Parse("2026-09-11T12:00:00-03:00"),
            Policy()));

        Assert.Equal(EmpiricalExecutionStatus.Pass, item.Artifact.Status);
        Assert.Equal(0.25, item.Artifact.Measurements["hit_at_1_rate"]);
        Assert.Equal(0.5, item.Artifact.Measurements["hit_at_3_rate"]);
        Assert.Equal(0.75, item.Artifact.Measurements["hit_at_5_rate"]);
        Assert.Equal(0.625, item.Artifact.Measurements["mrr"]);
        Assert.Equal(12.5, item.Artifact.Measurements["retrieval_duration_ms"]);
        Assert.Equal("reports/r0.json", item.Artifact.SourceArtifactReference);
        Assert.Equal("policies/r0.json", item.Artifact.MaterializationPolicyReference);
    }

    [Fact]
    public void Materializer_rejects_policy_that_does_not_match_report_configuration()
    {
        var report = new RetrievalBenchmarkReport(
            RetrievalBenchmarkReport.SupportedFormatVersion,
            "catalog-v1",
            Treatment(),
            [new RetrievalBenchmarkCaseReport(
                "case-1", 1, 1, 1, 1, 1.0, 1.0,
                [new("q1", 1, true, true, true)], null, null)]).Validate();
        var wrong = new RetrievalEmpiricalObservationPolicy(
            "R0", "postgres-ts-rank-cd-v1", "configs/r0.json", new string('9', 64),
            "hit_at_1_rate", "hit_at_3_rate", "hit_at_5_rate", "mrr", "retrieval_duration_ms", "candidate_execution_failure");

        Assert.Throws<InvalidOperationException>(() =>
            new RetrievalEmpiricalObservationMaterializer().Materialize(
                report,
                "reports/r0.json",
                new string('e', 64),
                "policies/r0.json",
                new string('f', 64),
                DateTimeOffset.Parse("2026-09-11T12:00:00-03:00"),
                wrong));
    }

    private static RetrievalBenchmarkCatalog Catalog(string documentId, string sha) =>
        new(
            "catalog-v1",
            [new RetrievalBenchmarkCase(
                "case-1",
                [new RetrievalBenchmarkQuery(
                    "q1",
                    "question",
                    [new RetrievalExpectedEvidence(documentId, sha)])])]);

    private static RetrievalBenchmarkTreatmentMetadata Treatment() =>
        new("R0", "postgres-ts-rank-cd-v1", "configs/r0.json", new string('1', 64));

    private static RetrievalEmpiricalObservationPolicy Policy() =>
        new(
            "R0",
            "postgres-ts-rank-cd-v1",
            "configs/r0.json",
            new string('1', 64),
            "hit_at_1_rate",
            "hit_at_3_rate",
            "hit_at_5_rate",
            "mrr",
            "retrieval_duration_ms",
            "candidate_execution_failure");

    private static LegalDocumentSearchHit Hit(
        string caseId,
        string documentId,
        string sha,
        float rank,
        string content = "content") =>
        new(new LegalDocumentSnapshot(caseId, documentId, "fixture", content, content, sha), rank);

    private sealed class FakeSearch(IReadOnlyDictionary<string, IReadOnlyList<LegalDocumentSearchHit>> results)
        : ILegalDocumentSearch
    {
        public Task<IReadOnlyList<LegalDocumentSearchHit>> SearchAsync(
            LegalCaseId caseId,
            string query,
            int limit,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(results.TryGetValue(query, out var hits)
                ? (IReadOnlyList<LegalDocumentSearchHit>)hits.Take(limit).ToArray()
                : []);
        }
    }

    private sealed class ThrowingSearch : ILegalDocumentSearch
    {
        public Task<IReadOnlyList<LegalDocumentSearchHit>> SearchAsync(
            LegalCaseId caseId,
            string query,
            int limit,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("synthetic retrieval failure");
    }
}
