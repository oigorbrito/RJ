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

        var item = Materialize(report, Policy()).Single();

        Assert.Equal(EmpiricalExecutionStatus.Pass, item.Artifact.Status);
        Assert.Empty(item.Artifact.FailedNonCompensableGates);
        Assert.Equal(0.75, item.Artifact.Measurements["claim_recall"]);
        Assert.Equal(1.0, item.Artifact.Measurements["citation_validity"]);
        Assert.Equal(0.5, item.Artifact.Measurements["groundedness"]);
        Assert.Equal("Gx", item.Artifact.TreatmentId);
        Assert.Equal("reports/gx.json", item.Artifact.SourceArtifactReference);
        Assert.Equal("policies/gx.json", item.Artifact.MaterializationPolicyReference);
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

        var item = Materialize(report, Policy()).Single();

        Assert.Equal(EmpiricalExecutionStatus.Fail, item.Artifact.Status);
        Assert.Empty(item.Artifact.Measurements);
        Assert.Equal(["candidate_execution_failure"], item.Artifact.FailedNonCompensableGates);
    }

    [Fact]
    public void Materialize_rejects_case_with_neither_evaluation_nor_execution_error_evidence()
    {
        var report = Report(new GenerationBenchmarkCaseReport("case-1", false, null, null, null));

        Assert.Throws<InvalidOperationException>(() => Materialize(report, Policy()));
    }

    [Fact]
    public void Materialize_rejects_treatment_policy_that_does_not_match_report_model_configuration()
    {
        var report = Report(new GenerationBenchmarkCaseReport(
            "case-1",
            true,
            new GenerationEvaluationResult("case-1", false, 1, 1, 1.0, 1.0, 1.0, true),
            null,
            null));
        var wrongPolicy = new GenerationEmpiricalObservationPolicy(
            "Gx",
            "model",
            "different-config",
            "configs/gx.json",
            new string('d', 64),
            "claim_recall",
            "citation_validity",
            "groundedness",
            "candidate_execution_failure");

        var error = Assert.Throws<InvalidOperationException>(() => Materialize(report, wrongPolicy));
        Assert.Contains("does not match", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Policy_rejects_selection_treatment_with_different_configuration_hash()
    {
        var treatment = new EmpiricalTreatmentDefinition(
            "Gx",
            EmpiricalTreatmentKind.Generation,
            "configs/gx.json",
            new string('e', 64),
            "generation challenger");

        Assert.Throws<InvalidOperationException>(() => Policy().RequireMatches(treatment));
    }

    private static IReadOnlyList<GenerationEmpiricalObservationMaterialization> Materialize(
        GenerationBenchmarkReport report,
        GenerationEmpiricalObservationPolicy policy) =>
        new GenerationEmpiricalObservationMaterializer().Materialize(
            report,
            "reports/gx.json",
            new string('a', 64),
            "policies/gx.json",
            new string('c', 64),
            DateTimeOffset.Parse("2026-09-11T12:00:00-03:00"),
            policy);

    private static GenerationEmpiricalObservationPolicy Policy() =>
        new(
            "Gx",
            "model",
            "config",
            "configs/gx.json",
            new string('d', 64),
            "claim_recall",
            "citation_validity",
            "groundedness",
            "candidate_execution_failure");

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
