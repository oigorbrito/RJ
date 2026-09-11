using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RJ.Application.Benchmarking;

public sealed record EmpiricalSelectionManifest(
    string FormatVersion,
    string ResearchQuestion,
    string CorpusManifestReference,
    string CorpusManifestSha256,
    string GitCommit,
    string Runtime,
    string DependencyEvidenceReference,
    string DependencyEvidenceSha256,
    string CommandEvidenceReference,
    string CommandEvidenceSha256,
    DateTimeOffset CreatedAt,
    EmpiricalTreatmentDefinition Baseline,
    EmpiricalTreatmentDefinition Challenger,
    IReadOnlyList<EmpiricalMetricDefinition> Metrics,
    IReadOnlyList<string> NonCompensableGates,
    IReadOnlyList<EmpiricalCaseObservation> Observations)
{
    public const string SupportedFormatVersion = "rjudi-empirical-selection-v1";

    public static EmpiricalSelectionManifest Parse(ReadOnlySpan<byte> utf8Json)
    {
        if (utf8Json.IsEmpty)
        {
            throw new ArgumentException("Empirical selection manifest cannot be empty.", nameof(utf8Json));
        }

        return JsonSerializer.Deserialize<EmpiricalSelectionManifest>(utf8Json, JsonOptions)
            ?? throw new InvalidOperationException("Empirical selection manifest produced no document.");
    }

    public EmpiricalSelectionManifest Validate()
    {
        if (!StringComparer.Ordinal.Equals(FormatVersion, SupportedFormatVersion))
        {
            throw new InvalidOperationException($"Unsupported empirical selection format '{FormatVersion}'.");
        }

        EmpiricalTreatmentDefinition.Require(ResearchQuestion, nameof(ResearchQuestion));
        EmpiricalTreatmentDefinition.Require(CorpusManifestReference, nameof(CorpusManifestReference));
        EmpiricalTreatmentDefinition.RequireSha256(CorpusManifestSha256, nameof(CorpusManifestSha256));
        RequireGitCommit(GitCommit);
        EmpiricalTreatmentDefinition.Require(Runtime, nameof(Runtime));
        EmpiricalTreatmentDefinition.Require(DependencyEvidenceReference, nameof(DependencyEvidenceReference));
        EmpiricalTreatmentDefinition.RequireSha256(DependencyEvidenceSha256, nameof(DependencyEvidenceSha256));
        EmpiricalTreatmentDefinition.Require(CommandEvidenceReference, nameof(CommandEvidenceReference));
        EmpiricalTreatmentDefinition.RequireSha256(CommandEvidenceSha256, nameof(CommandEvidenceSha256));
        if (CreatedAt == default)
        {
            throw new InvalidOperationException("Manifest created-at timestamp must be recorded.");
        }

        ArgumentNullException.ThrowIfNull(Baseline);
        ArgumentNullException.ThrowIfNull(Challenger);
        ArgumentNullException.ThrowIfNull(Metrics);
        ArgumentNullException.ThrowIfNull(NonCompensableGates);
        ArgumentNullException.ThrowIfNull(Observations);

        if (Baseline.Kind != Challenger.Kind)
        {
            throw new InvalidOperationException("Baseline and challenger must belong to the same treatment kind.");
        }

        if (StringComparer.Ordinal.Equals(Baseline.TreatmentId, Challenger.TreatmentId))
        {
            throw new InvalidOperationException("Baseline and challenger ids must differ.");
        }

        if (Metrics.Count == 0 || Metrics.All(metric => !metric.Required))
        {
            throw new InvalidOperationException("At least one pre-specified required metric is necessary.");
        }

        if (Metrics.GroupBy(metric => metric.MetricId, StringComparer.Ordinal).Any(group => group.Count() > 1))
        {
            throw new InvalidOperationException("Metric ids must be unique.");
        }

        if (NonCompensableGates.Count == 0
            || NonCompensableGates.Any(string.IsNullOrWhiteSpace)
            || NonCompensableGates.Distinct(StringComparer.Ordinal).Count() != NonCompensableGates.Count)
        {
            throw new InvalidOperationException("Non-compensable gate ids must be non-empty and unique.");
        }

        if (Observations.Count == 0)
        {
            throw new InvalidOperationException("Paired raw observations are required.");
        }

        var allowedTreatmentIds = new HashSet<string>(StringComparer.Ordinal)
        {
            Baseline.TreatmentId,
            Challenger.TreatmentId
        };
        if (Observations.Any(item => !allowedTreatmentIds.Contains(item.TreatmentId)))
        {
            throw new InvalidOperationException("Manifest observations may reference only the declared baseline and challenger.");
        }

        var declaredGates = NonCompensableGates.ToHashSet(StringComparer.Ordinal);
        var declaredMetrics = Metrics.Select(item => item.MetricId).ToHashSet(StringComparer.Ordinal);
        foreach (var observation in Observations)
        {
            if (observation.FailedNonCompensableGates.Any(gate => !declaredGates.Contains(gate)))
            {
                throw new InvalidOperationException(
                    $"Observation '{observation.CaseId}/{observation.TreatmentId}' reports an undeclared non-compensable gate.");
            }

            if (observation.Measurements.Any(item => !declaredMetrics.Contains(item.Key)))
            {
                throw new InvalidOperationException(
                    $"Observation '{observation.CaseId}/{observation.TreatmentId}' reports an undeclared metric.");
            }

            if (observation.Measurements.Any(item => !double.IsFinite(item.Value)))
            {
                throw new InvalidOperationException(
                    $"Observation '{observation.CaseId}/{observation.TreatmentId}' contains a non-finite measurement.");
            }
        }

        return this;
    }

    public EmpiricalSelectionManifest RequireMatchesCorpus(Eval010CorpusManifest corpus)
    {
        ArgumentNullException.ThrowIfNull(corpus);
        corpus.Validate();

        var corpusCaseIds = corpus.Cases
            .Select(item => item.CaseId)
            .ToHashSet(StringComparer.Ordinal);
        var observationCaseIds = Observations
            .Select(item => item.CaseId)
            .ToHashSet(StringComparer.Ordinal);

        if (!corpusCaseIds.SetEquals(observationCaseIds))
        {
            var missing = corpusCaseIds.Except(observationCaseIds, StringComparer.Ordinal).OrderBy(item => item, StringComparer.Ordinal).ToArray();
            var extra = observationCaseIds.Except(corpusCaseIds, StringComparer.Ordinal).OrderBy(item => item, StringComparer.Ordinal).ToArray();
            throw new InvalidOperationException(
                $"Selection observation case set must exactly match admitted EVAL-010 corpus. Missing: [{string.Join(", ", missing)}]; extra: [{string.Join(", ", extra)}].");
        }

        foreach (var caseId in corpusCaseIds)
        {
            var paired = Observations
                .Where(item => StringComparer.Ordinal.Equals(item.CaseId, caseId))
                .ToArray();
            if (paired.Length != 2
                || paired.Count(item => StringComparer.Ordinal.Equals(item.TreatmentId, Baseline.TreatmentId)) != 1
                || paired.Count(item => StringComparer.Ordinal.Equals(item.TreatmentId, Challenger.TreatmentId)) != 1)
            {
                throw new InvalidOperationException(
                    $"EVAL-010 case '{caseId}' must have exactly one baseline and one challenger observation.");
            }
        }

        return this;
    }

    public static string ComputeSha256(ReadOnlySpan<byte> bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static void RequireGitCommit(string value)
    {
        var normalized = EmpiricalTreatmentDefinition.Require(value, nameof(GitCommit));
        if (normalized.Length != 40 || normalized.Any(character => !Uri.IsHexDigit(character)))
        {
            throw new InvalidOperationException("Git commit must be an exact 40-character hexadecimal commit SHA.");
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };
}
