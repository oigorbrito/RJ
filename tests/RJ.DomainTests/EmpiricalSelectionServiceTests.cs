using RJ.Application.Benchmarking;

namespace RJ.DomainTests;

public sealed class EmpiricalSelectionServiceTests
{
    private readonly EmpiricalSelectionService _service = new();

    [Fact]
    public void Compare_selects_challenger_only_when_it_pareto_dominates()
    {
        var report = _service.Compare(
            Treatment("r0", EmpiricalTreatmentKind.Retrieval),
            Treatment("r1", EmpiricalTreatmentKind.Retrieval),
            Metrics(),
            [
                Observation("case-1", "r0", 0.80, 100),
                Observation("case-1", "r1", 0.85, 90),
                Observation("case-2", "r0", 0.70, 120),
                Observation("case-2", "r1", 0.70, 110)
            ]);

        Assert.Equal(EmpiricalSelectionDecision.SelectChallenger, report.Decision);
        Assert.Equal(3, report.BetterCoordinates);
        Assert.Equal(0, report.WorseCoordinates);
        Assert.Equal(1, report.EqualCoordinates);
    }

    [Fact]
    public void Compare_keeps_baseline_when_baseline_pareto_dominates()
    {
        var report = _service.Compare(
            Treatment("g0", EmpiricalTreatmentKind.Generation),
            Treatment("g1", EmpiricalTreatmentKind.Generation),
            Metrics(),
            [
                Observation("case-1", "g0", 0.90, 80),
                Observation("case-1", "g1", 0.85, 90)
            ]);

        Assert.Equal(EmpiricalSelectionDecision.KeepBaseline, report.Decision);
        Assert.Equal(0, report.BetterCoordinates);
        Assert.Equal(2, report.WorseCoordinates);
    }

    [Fact]
    public void Compare_returns_no_clear_winner_for_tradeoff_without_weighting()
    {
        var report = _service.Compare(
            Treatment("r0", EmpiricalTreatmentKind.Retrieval),
            Treatment("r2", EmpiricalTreatmentKind.Retrieval),
            Metrics(),
            [
                Observation("case-1", "r0", 0.80, 100),
                Observation("case-1", "r2", 0.90, 130)
            ]);

        Assert.Equal(EmpiricalSelectionDecision.NoClearWinner, report.Decision);
        Assert.Equal(1, report.BetterCoordinates);
        Assert.Equal(1, report.WorseCoordinates);
        Assert.Contains(report.Reasons, item => item.Contains("weighted score", StringComparison.Ordinal));
    }

    [Fact]
    public void Compare_returns_no_clear_winner_when_measurements_are_equal()
    {
        var report = _service.Compare(
            Treatment("r0", EmpiricalTreatmentKind.Retrieval),
            Treatment("r1", EmpiricalTreatmentKind.Retrieval),
            Metrics(),
            [
                Observation("case-1", "r0", 0.80, 100),
                Observation("case-1", "r1", 0.80, 100)
            ]);

        Assert.Equal(EmpiricalSelectionDecision.NoClearWinner, report.Decision);
        Assert.Equal(0, report.BetterCoordinates);
        Assert.Equal(0, report.WorseCoordinates);
        Assert.Equal(2, report.EqualCoordinates);
    }

    [Fact]
    public void Compare_blocks_unpaired_case_instead_of_dropping_it()
    {
        var report = _service.Compare(
            Treatment("r0", EmpiricalTreatmentKind.Retrieval),
            Treatment("r1", EmpiricalTreatmentKind.Retrieval),
            Metrics(),
            [
                Observation("case-1", "r0", 0.80, 100),
                Observation("case-1", "r1", 0.85, 90),
                Observation("case-2", "r0", 0.70, 120)
            ]);

        Assert.Equal(EmpiricalSelectionDecision.Blocked, report.Decision);
        Assert.Contains(report.Reasons, item => item.Contains("not paired", StringComparison.Ordinal));
    }

    [Fact]
    public void Compare_blocks_not_tested_observation_instead_of_treating_it_as_pass()
    {
        var notTested = Observation(
            "case-1",
            "r1",
            0.85,
            90,
            status: EmpiricalExecutionStatus.NotTested);

        var report = _service.Compare(
            Treatment("r0", EmpiricalTreatmentKind.Retrieval),
            Treatment("r1", EmpiricalTreatmentKind.Retrieval),
            Metrics(),
            [Observation("case-1", "r0", 0.80, 100), notTested]);

        Assert.Equal(EmpiricalSelectionDecision.Blocked, report.Decision);
        Assert.Contains(report.Reasons, item => item.Contains("NOTTESTED", StringComparison.Ordinal));
    }

    [Fact]
    public void Compare_keeps_safe_baseline_when_challenger_fails_non_compensable_gate()
    {
        var failed = Observation(
            "case-1",
            "g1",
            0.95,
            70,
            status: EmpiricalExecutionStatus.Fail,
            failedGates: ["no_oracle_leakage"]);

        var report = _service.Compare(
            Treatment("g0", EmpiricalTreatmentKind.Generation),
            Treatment("g1", EmpiricalTreatmentKind.Generation),
            Metrics(),
            [Observation("case-1", "g0", 0.80, 100), failed]);

        Assert.Equal(EmpiricalSelectionDecision.KeepBaseline, report.Decision);
        Assert.Equal(0, report.BetterCoordinates);
    }

    [Fact]
    public void Compare_blocks_missing_required_metric()
    {
        var challenger = Observation(
            "case-1",
            "r1",
            0.85,
            90,
            measurements: new Dictionary<string, double>
            {
                ["quality"] = 0.85
            });

        var report = _service.Compare(
            Treatment("r0", EmpiricalTreatmentKind.Retrieval),
            Treatment("r1", EmpiricalTreatmentKind.Retrieval),
            Metrics(),
            [Observation("case-1", "r0", 0.80, 100), challenger]);

        Assert.Equal(EmpiricalSelectionDecision.Blocked, report.Decision);
        Assert.Contains(report.Reasons, item => item.Contains("latency_ms", StringComparison.Ordinal));
    }

    private static EmpiricalTreatmentDefinition Treatment(string id, EmpiricalTreatmentKind kind) =>
        new(id, kind, $"configs/{id}.json", Sha(id), $"Treatment {id}");

    private static IReadOnlyList<EmpiricalMetricDefinition> Metrics() =>
    [
        new("quality", EmpiricalMetricDirection.HigherIsBetter, true),
        new("latency_ms", EmpiricalMetricDirection.LowerIsBetter, true),
        new("diagnostic_only", EmpiricalMetricDirection.HigherIsBetter, false)
    ];

    private static EmpiricalCaseObservation Observation(
        string caseId,
        string treatmentId,
        double quality,
        double latency,
        EmpiricalExecutionStatus status = EmpiricalExecutionStatus.Pass,
        IReadOnlyList<string>? failedGates = null,
        IReadOnlyDictionary<string, double>? measurements = null) =>
        new(
            caseId,
            treatmentId,
            status,
            measurements ?? new Dictionary<string, double>
            {
                ["quality"] = quality,
                ["latency_ms"] = latency
            },
            failedGates ?? [],
            $"artifacts/{caseId}/{treatmentId}.json",
            Sha($"{caseId}:{treatmentId}"));

    private static string Sha(string seed)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(seed);
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant();
    }
}
