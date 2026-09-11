namespace RJ.Application.Benchmarking;

public enum EmpiricalTreatmentKind
{
    Retrieval,
    Generation
}

public enum EmpiricalMetricDirection
{
    HigherIsBetter,
    LowerIsBetter
}

public enum EmpiricalExecutionStatus
{
    Pass,
    Fail,
    Blocked,
    NotTested
}

public enum EmpiricalSelectionDecision
{
    SelectChallenger,
    KeepBaseline,
    NoClearWinner,
    Blocked
}

public sealed record EmpiricalTreatmentDefinition(
    string TreatmentId,
    EmpiricalTreatmentKind Kind,
    string ConfigurationDigest,
    string Description)
{
    public string TreatmentId { get; } = Require(TreatmentId, nameof(TreatmentId));
    public string ConfigurationDigest { get; } = Require(ConfigurationDigest, nameof(ConfigurationDigest));
    public string Description { get; } = Require(Description, nameof(Description));

    private static string Require(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Value cannot be empty.", parameterName);
        }

        return value.Trim();
    }
}

public sealed record EmpiricalMetricDefinition(
    string MetricId,
    EmpiricalMetricDirection Direction,
    bool Required)
{
    public string MetricId { get; } = string.IsNullOrWhiteSpace(MetricId)
        ? throw new ArgumentException("Metric id cannot be empty.", nameof(MetricId))
        : MetricId.Trim();
}

public sealed record EmpiricalCaseObservation(
    string CaseId,
    string TreatmentId,
    EmpiricalExecutionStatus Status,
    IReadOnlyDictionary<string, double> Measurements,
    IReadOnlyList<string> FailedNonCompensableGates,
    string ArtifactReference)
{
    public string CaseId { get; } = string.IsNullOrWhiteSpace(CaseId)
        ? throw new ArgumentException("Case id cannot be empty.", nameof(CaseId))
        : CaseId.Trim();

    public string TreatmentId { get; } = string.IsNullOrWhiteSpace(TreatmentId)
        ? throw new ArgumentException("Treatment id cannot be empty.", nameof(TreatmentId))
        : TreatmentId.Trim();

    public IReadOnlyDictionary<string, double> Measurements { get; } =
        Measurements ?? throw new ArgumentNullException(nameof(Measurements));

    public IReadOnlyList<string> FailedNonCompensableGates { get; } =
        FailedNonCompensableGates ?? throw new ArgumentNullException(nameof(FailedNonCompensableGates));

    public string ArtifactReference { get; } = string.IsNullOrWhiteSpace(ArtifactReference)
        ? throw new ArgumentException("Artifact reference cannot be empty.", nameof(ArtifactReference))
        : ArtifactReference.Trim();
}

public sealed record EmpiricalSelectionReport(
    EmpiricalTreatmentKind Kind,
    string BaselineTreatmentId,
    string ChallengerTreatmentId,
    EmpiricalSelectionDecision Decision,
    IReadOnlyList<string> Reasons,
    IReadOnlyList<string> ComparedCaseIds,
    IReadOnlyList<string> ComparedMetricIds,
    int BetterCoordinates,
    int WorseCoordinates,
    int EqualCoordinates);
