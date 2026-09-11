using RJ.Application.Benchmarking;
using RJ.Application.Evaluation;

namespace RJ.DomainTests;

public sealed class GenerationEmpiricalObservationMaterializerTests
{
    [Fact]
    public void Materialize_maps_evaluated_case_to_measurements_without_inventing_hard_gate_failure()
    {
        var report = Report(new GenerationBenchmarkCaseReport(
            "case-1",
            false,
            new GenerationEvaluationResult("case-1", false, 1, 1, 0.75, 1.0, 0.5, false),
            null,
            null));

        var item = new GenerationEmpiricalObservationMaterializer().Materialize(
            report,
            "Gx",
            "reports/gx.json",
            new string('a', 64),
            DateTimeOffset.Parse("2026-09-11T12:00:00-03:00"),
            Policy()).Single();

        Assert.Equal(EmpiricalExecutionStatus.Pass, item.Artifact.Status);
        Assert.Empty(item.Artifact.FailedNonCompensableGates);
        Assert.Equal(0.75, item.Artifact.Measurements["claim_recall"]);
        Assert.Equal(1.0, item.Artifact.Measurements["citation_validity"]);
        Assert.Equal(0.5, item.Artifact.Measurements["groundedness"]);
        Assert.Equal("reports/gx.json", item.Artifact.SourceArtifactReference);
        Assert.Equal(EmpiricalSelectionManifest.ComputeSha256(item.ArtifactUtf8Json), item.ArtifactSha256);
    }

    [Fact]
    public void Materialize_maps_candidate_execution_error_to_explicit_non_compensable_gate()
    {
        var report = Report(new GenerationBenchmarkCaseReport(
            "case-1",
            false,
            null,
            "System.TimeoutException",
            "Candidate execution failed."));

        var item = new GenerationEmpiricalObservationMaterializer().Materialize(
            report,
            "Gx",
            "reports/gx.json",
            new string('a', 64),
            DateTimeOffset.Parse("2026-09-11T12:00:00-03:00"),
            Policy()).Single();

        Assert.Equal(EmpiricalExecutionStatus.Fail, item.Artifact.Status);
        Assert.Empty(item.Artifact.Measurements);
        Assert.Equal(["candidate_execution_failure"], item.Artifact.FailedNonCompensableGates);
    }

    [Fact]
    public void Materialize_rejects_case_with_neither_evaluation_nor_execution_error_evidence()
    {
        var report = Report(new GenerationBenchmarkCaseReport("case-1", false, null, null, null));

        Assert.Throws<InvalidOperationException>(() =>
            new GenerationEmpiricalObservationMaterializer().Materialize(
                report,
                "Gx",
                "reports/gx.json",
                new string('a', 64),
                DateTimeOffset.Parse("2026-09-11T12:00:00-03:00"),
                Policy()));
    }

    private static GenerationEmpiricalObservationPolicy Policy() =>
        new("claim_recall", "citation_validity", "groundedness", "candidate_execution_failure");

    private static GenerationBenchmarkReport Report(GenerationBenchmarkCaseReport item) =>
        new(
            new GenerationBenchmarkMetadata(new string('b', 40), ".NET 10.0.0", "catalog-v1", "model", "config", "seed"),
            1,
            item.Passed ? 1 : 0,
            item.Passed ? 0 : 1,
            item.Evaluation?.ClaimRecall ?? 0,
            item.Evaluation?.CitationValidity ?? 0,
            item.Evaluation?.Groundedness ?? 0,
            item.Passed,
            [item]);
}
