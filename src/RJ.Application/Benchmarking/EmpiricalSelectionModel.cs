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
    string ConfigurationReference,
    string ConfigurationSha256,
    string Description)
{
    public string TreatmentId { get; } = Require(TreatmentId, nameof(TreatmentId));
    public EmpiricalTreatmentKind Kind { get; } = Enum.IsDefined(Kind)
        ? Kind
        : throw new ArgumentOutOfRangeException(nameof(Kind), Kind, "Unsupported empirical treatment kind.");
    public string ConfigurationReference { get; } = Require(ConfigurationReference, nameof(ConfigurationReference));
    public string ConfigurationSha256 { get; } = RequireSha256(ConfigurationSha256, nameof(ConfigurationSha256));
    public string Description { get; } = Require(Description, nameof(Description));

    internal static string Require(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Value cannot be empty.", parameterName);
        }

        return value.Trim();
    }

    internal static string RequireSha256(string value, string parameterName)
    {
        var normalized = Require(value, parameterName).ToLowerInvariant();
        if (normalized.Length != 64 || normalized.Any(character => !Uri.IsHexDigit(character)))
        {
            throw new ArgumentException("SHA-256 must contain exactly 64 hexadecimal characters.", parameterName);
        }

        return normalized;
    }
}

public sealed record EmpiricalMetricDefinition(
    string MetricId,
    EmpiricalMetricDirection Direction,
    bool Required)
{
    public string MetricId { get; } = EmpiricalTreatmentDefinition.Require(MetricId, nameof(MetricId));
    public EmpiricalMetricDirection Direction { get; } = Enum.IsDefined(Direction)
        ? Direction
        : throw new ArgumentOutOfRangeException(nameof(Direction), Direction, "Unsupported empirical metric direction.");
}

public sealed record EmpiricalCaseObservation(
    string CaseId,
    string TreatmentId,
    EmpiricalExecutionStatus Status,
    IReadOnlyDictionary<string, double> Measurements,
    IReadOnlyList<string> FailedNonCompensableGates,
    string ArtifactReference,
    string ArtifactSha256)
{
    public string CaseId { get; } = EmpiricalTreatmentDefinition.Require(CaseId, nameof(CaseId));
    public string TreatmentId { get; } = EmpiricalTreatmentDefinition.Require(TreatmentId, nameof(TreatmentId));
    public EmpiricalExecutionStatus Status { get; } = Enum.IsDefined(Status)
        ? Status
        : throw new ArgumentOutOfRangeException(nameof(Status), Status, "Unsupported empirical execution status.");

    public IReadOnlyDictionary<string, double> Measurements { get; } =
        Measurements ?? throw new ArgumentNullException(nameof(Measurements));

    public IReadOnlyList<string> FailedNonCompensableGates { get; } =
        FailedNonCompensableGates ?? throw new ArgumentNullException(nameof(FailedNonCompensableGates));

    public string ArtifactReference { get; } = EmpiricalTreatmentDefinition.Require(ArtifactReference, nameof(ArtifactReference));
    public string ArtifactSha256 { get; } = EmpiricalTreatmentDefinition.RequireSha256(ArtifactSha256, nameof(ArtifactSha256));
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
