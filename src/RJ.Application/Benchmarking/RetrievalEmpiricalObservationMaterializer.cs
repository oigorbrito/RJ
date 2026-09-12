using System.Text.Json;
using System.Text.Json.Serialization;

namespace RJ.Application.Benchmarking;

public sealed record RetrievalEmpiricalObservationPolicy(
    string TreatmentId,
    string ImplementationId,
    string ConfigurationReference,
    string ConfigurationSha256,
    string HitAt1RateMetricId,
    string HitAt3RateMetricId,
    string HitAt5RateMetricId,
    string MrrMetricId,
    string RetrievalDurationMsMetricId,
    string CandidateExecutionFailureGateId)
{
    public string TreatmentId { get; } = EmpiricalTreatmentDefinition.Require(TreatmentId, nameof(TreatmentId));
    public string ImplementationId { get; } = EmpiricalTreatmentDefinition.Require(ImplementationId, nameof(ImplementationId));
    public string ConfigurationReference { get; } = EmpiricalTreatmentDefinition.Require(ConfigurationReference, nameof(ConfigurationReference));
    public string ConfigurationSha256 { get; } = EmpiricalTreatmentDefinition.RequireSha256(ConfigurationSha256, nameof(ConfigurationSha256));
    public string HitAt1RateMetricId { get; } = EmpiricalTreatmentDefinition.Require(HitAt1RateMetricId, nameof(HitAt1RateMetricId));
    public string HitAt3RateMetricId { get; } = EmpiricalTreatmentDefinition.Require(HitAt3RateMetricId, nameof(HitAt3RateMetricId));
    public string HitAt5RateMetricId { get; } = EmpiricalTreatmentDefinition.Require(HitAt5RateMetricId, nameof(HitAt5RateMetricId));
    public string MrrMetricId { get; } = EmpiricalTreatmentDefinition.Require(MrrMetricId, nameof(MrrMetricId));
    public string RetrievalDurationMsMetricId { get; } = EmpiricalTreatmentDefinition.Require(RetrievalDurationMsMetricId, nameof(RetrievalDurationMsMetricId));
    public string CandidateExecutionFailureGateId { get; } = EmpiricalTreatmentDefinition.Require(CandidateExecutionFailureGateId, nameof(CandidateExecutionFailureGateId));

    public RetrievalEmpiricalObservationPolicy Validate()
    {
        var ids = new[] { HitAt1RateMetricId, HitAt3RateMetricId, HitAt5RateMetricId, MrrMetricId, RetrievalDurationMsMetricId };
        if (ids.Distinct(StringComparer.Ordinal).Count() != ids.Length)
        {
            throw new InvalidOperationException("Retrieval empirical metric ids must be unique.");
        }

        return this;
    }

    public void RequireMatches(RetrievalBenchmarkReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        report.Validate();
        if (!StringComparer.Ordinal.Equals(TreatmentId, report.Treatment.TreatmentId)
            || !StringComparer.Ordinal.Equals(ImplementationId, report.Treatment.ImplementationId)
            || !StringComparer.Ordinal.Equals(ConfigurationReference, report.Treatment.ConfigurationReference)
            || !StringComparer.Ordinal.Equals(ConfigurationSha256, report.Treatment.ConfigurationSha256))
        {
            throw new InvalidOperationException(
                $"Retrieval treatment '{TreatmentId}' policy does not match benchmark treatment identity/configuration.");
        }
    }

    public void RequireMatches(EmpiricalTreatmentDefinition treatment)
    {
        ArgumentNullException.ThrowIfNull(treatment);
        if (treatment.Kind != EmpiricalTreatmentKind.Retrieval
            || !StringComparer.Ordinal.Equals(TreatmentId, treatment.TreatmentId)
            || !StringComparer.Ordinal.Equals(ConfigurationReference, treatment.ConfigurationReference)
            || !StringComparer.Ordinal.Equals(ConfigurationSha256, treatment.ConfigurationSha256))
        {
            throw new InvalidOperationException(
                $"Retrieval materialization policy does not match empirical treatment '{treatment.TreatmentId}'.");
        }
    }
}

public sealed record RetrievalEmpiricalObservationMaterialization(
    string CaseId,
    string TreatmentId,
    EmpiricalRawObservationArtifact Artifact,
    byte[] ArtifactUtf8Json,
    string ArtifactSha256);

public sealed class RetrievalEmpiricalObservationMaterializer
{
    public static IReadOnlyList<RetrievalEmpiricalObservationMaterialization> Materialize(
        RetrievalBenchmarkReport report,
        string sourceReportReference,
        string sourceReportSha256,
        string materializationPolicyReference,
        string materializationPolicySha256,
        DateTimeOffset recordedAt,
        RetrievalEmpiricalObservationPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(policy);
        policy.Validate().RequireMatches(report);

        sourceReportReference = EmpiricalTreatmentDefinition.Require(sourceReportReference, nameof(sourceReportReference));
        sourceReportSha256 = EmpiricalTreatmentDefinition.RequireSha256(sourceReportSha256, nameof(sourceReportSha256));
        materializationPolicyReference = EmpiricalTreatmentDefinition.Require(materializationPolicyReference, nameof(materializationPolicyReference));
        materializationPolicySha256 = EmpiricalTreatmentDefinition.RequireSha256(materializationPolicySha256, nameof(materializationPolicySha256));
        if (recordedAt == default)
        {
            throw new ArgumentException("Recorded-at timestamp must be provided.", nameof(recordedAt));
        }

        var output = new List<RetrievalEmpiricalObservationMaterialization>(report.Cases.Count);
        foreach (var item in report.Cases.OrderBy(caseReport => caseReport.CaseId, StringComparer.Ordinal))
        {
            EmpiricalExecutionStatus status;
            IReadOnlyDictionary<string, double> measurements;
            IReadOnlyList<string> failedGates;

            if (!item.ExecutionSucceeded)
            {
                status = EmpiricalExecutionStatus.Fail;
                measurements = new Dictionary<string, double>(StringComparer.Ordinal);
                failedGates = [policy.CandidateExecutionFailureGateId];
            }
            else
            {
                status = EmpiricalExecutionStatus.Pass;
                measurements = new Dictionary<string, double>(StringComparer.Ordinal)
                {
                    [policy.HitAt1RateMetricId] = (double)item.HitAt1 / item.QueryCount,
                    [policy.HitAt3RateMetricId] = (double)item.HitAt3 / item.QueryCount,
                    [policy.HitAt5RateMetricId] = (double)item.HitAt5 / item.QueryCount,
                    [policy.MrrMetricId] = item.Mrr,
                    [policy.RetrievalDurationMsMetricId] = item.DurationMs
                };
                failedGates = [];
            }

            var artifact = new EmpiricalRawObservationArtifact(
                EmpiricalRawObservationArtifact.SupportedFormatVersion,
                item.CaseId,
                policy.TreatmentId,
                status,
                measurements,
                failedGates,
                recordedAt,
                sourceReportReference,
                sourceReportSha256,
                materializationPolicyReference,
                materializationPolicySha256).Validate();

            var bytes = JsonSerializer.SerializeToUtf8Bytes(artifact, JsonOptions);
            output.Add(new RetrievalEmpiricalObservationMaterialization(
                item.CaseId,
                policy.TreatmentId,
                artifact,
                bytes,
                EmpiricalSelectionManifest.ComputeSha256(bytes)));
        }

        return output;
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) }
    };
}
