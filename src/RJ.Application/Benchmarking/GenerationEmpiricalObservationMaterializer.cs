using System.Text.Json;
using System.Text.Json.Serialization;

namespace RJ.Application.Benchmarking;

public sealed record GenerationEmpiricalObservationPolicy(
    string TreatmentId,
    string ModelId,
    string ModelConfiguration,
    string ClaimRecallMetricId,
    string CitationValidityMetricId,
    string GroundednessMetricId,
    string CandidateExecutionFailureGateId)
{
    public string TreatmentId { get; } = EmpiricalTreatmentDefinition.Require(TreatmentId, nameof(TreatmentId));
    public string ModelId { get; } = EmpiricalTreatmentDefinition.Require(ModelId, nameof(ModelId));
    public string ModelConfiguration { get; } = EmpiricalTreatmentDefinition.Require(ModelConfiguration, nameof(ModelConfiguration));
    public string ClaimRecallMetricId { get; } = EmpiricalTreatmentDefinition.Require(ClaimRecallMetricId, nameof(ClaimRecallMetricId));
    public string CitationValidityMetricId { get; } = EmpiricalTreatmentDefinition.Require(CitationValidityMetricId, nameof(CitationValidityMetricId));
    public string GroundednessMetricId { get; } = EmpiricalTreatmentDefinition.Require(GroundednessMetricId, nameof(GroundednessMetricId));
    public string CandidateExecutionFailureGateId { get; } = EmpiricalTreatmentDefinition.Require(CandidateExecutionFailureGateId, nameof(CandidateExecutionFailureGateId));

    public GenerationEmpiricalObservationPolicy Validate()
    {
        var ids = new[] { ClaimRecallMetricId, CitationValidityMetricId, GroundednessMetricId };
        if (ids.Distinct(StringComparer.Ordinal).Count() != ids.Length)
        {
            throw new InvalidOperationException("Generation empirical metric ids must be unique.");
        }

        return this;
    }

    public void RequireMatches(GenerationBenchmarkReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(report.Metadata);
        report.Metadata.Validate();

        if (!StringComparer.Ordinal.Equals(ModelId, report.Metadata.ModelId)
            || !StringComparer.Ordinal.Equals(ModelConfiguration, report.Metadata.ModelConfiguration))
        {
            throw new InvalidOperationException(
                $"Generation treatment '{TreatmentId}' policy does not match benchmark model/configuration.");
        }
    }
}

public sealed record GenerationEmpiricalObservationMaterialization(
    string CaseId,
    string TreatmentId,
    EmpiricalRawObservationArtifact Artifact,
    byte[] ArtifactUtf8Json,
    string ArtifactSha256);

public sealed class GenerationEmpiricalObservationMaterializer
{
    public IReadOnlyList<GenerationEmpiricalObservationMaterialization> Materialize(
        GenerationBenchmarkReport report,
        string sourceReportReference,
        string sourceReportSha256,
        string materializationPolicyReference,
        string materializationPolicySha256,
        DateTimeOffset recordedAt,
        GenerationEmpiricalObservationPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(report.Metadata);
        ArgumentNullException.ThrowIfNull(report.Cases);
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

        if (report.TotalCases != report.Cases.Count
            || report.PassedCases + report.FailedCases != report.TotalCases)
        {
            throw new InvalidOperationException("Generation benchmark report aggregate case counts are inconsistent.");
        }

        if (report.Cases.Count == 0)
        {
            throw new InvalidOperationException("Generation benchmark report contains no case reports.");
        }

        var duplicateCase = report.Cases
            .GroupBy(item => item.CaseId, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicateCase is not null)
        {
            throw new InvalidOperationException($"Generation benchmark report contains duplicate case '{duplicateCase.Key}'.");
        }

        var output = new List<GenerationEmpiricalObservationMaterialization>(report.Cases.Count);
        foreach (var item in report.Cases.OrderBy(caseReport => caseReport.CaseId, StringComparer.Ordinal))
        {
            var caseId = EmpiricalTreatmentDefinition.Require(item.CaseId, "caseId");
            EmpiricalExecutionStatus status;
            IReadOnlyDictionary<string, double> measurements;
            IReadOnlyList<string> failedGates;

            if (item.Evaluation is null)
            {
                if (string.IsNullOrWhiteSpace(item.ErrorType) || item.Passed)
                {
                    throw new InvalidOperationException(
                        $"Generation benchmark case '{caseId}' has inconsistent execution-failure evidence.");
                }

                status = EmpiricalExecutionStatus.Fail;
                measurements = new Dictionary<string, double>(StringComparer.Ordinal);
                failedGates = [policy.CandidateExecutionFailureGateId];
            }
            else
            {
                if (!StringComparer.Ordinal.Equals(item.Evaluation.CaseId, caseId))
                {
                    throw new InvalidOperationException(
                        $"Generation benchmark evaluation case id does not match report case '{caseId}'.");
                }

                if (!string.IsNullOrWhiteSpace(item.ErrorType) || item.Passed != item.Evaluation.Passed)
                {
                    throw new InvalidOperationException(
                        $"Generation benchmark case '{caseId}' has inconsistent evaluation status/error evidence.");
                }

                status = EmpiricalExecutionStatus.Pass;
                measurements = new Dictionary<string, double>(StringComparer.Ordinal)
                {
                    [policy.ClaimRecallMetricId] = RequireFiniteUnitInterval(item.Evaluation.ClaimRecall, "claim recall", caseId),
                    [policy.CitationValidityMetricId] = RequireFiniteUnitInterval(item.Evaluation.CitationValidity, "citation validity", caseId),
                    [policy.GroundednessMetricId] = RequireFiniteUnitInterval(item.Evaluation.Groundedness, "groundedness", caseId)
                };
                failedGates = [];
            }

            var artifact = new EmpiricalRawObservationArtifact(
                EmpiricalRawObservationArtifact.SupportedFormatVersion,
                caseId,
                policy.TreatmentId,
                status,
                measurements,
                failedGates,
                recordedAt,
                sourceReportReference,
                sourceReportSha256,
                materializationPolicyReference,
                materializationPolicySha256);
            artifact.Validate();

            var bytes = JsonSerializer.SerializeToUtf8Bytes(artifact, JsonOptions);
            output.Add(new GenerationEmpiricalObservationMaterialization(
                caseId,
                policy.TreatmentId,
                artifact,
                bytes,
                EmpiricalSelectionManifest.ComputeSha256(bytes)));
        }

        return output;
    }

    private static double RequireFiniteUnitInterval(double value, string metric, string caseId)
    {
        if (!double.IsFinite(value) || value < 0.0 || value > 1.0)
        {
            throw new InvalidOperationException(
                $"Generation benchmark {metric} for case '{caseId}' must be finite and within [0,1].");
        }

        return value;
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) }
    };
}
