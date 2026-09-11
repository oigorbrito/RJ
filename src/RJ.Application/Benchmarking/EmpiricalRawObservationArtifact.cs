using System.Text.Json;
using System.Text.Json.Serialization;

namespace RJ.Application.Benchmarking;

public sealed record EmpiricalRawObservationArtifact(
    string FormatVersion,
    string CaseId,
    string TreatmentId,
    EmpiricalExecutionStatus Status,
    IReadOnlyDictionary<string, double> Measurements,
    IReadOnlyList<string> FailedNonCompensableGates,
    DateTimeOffset RecordedAt)
{
    public const string SupportedFormatVersion = "rjudi-empirical-raw-observation-v1";

    public static EmpiricalRawObservationArtifact Parse(ReadOnlySpan<byte> utf8Json)
    {
        if (utf8Json.IsEmpty)
        {
            throw new ArgumentException("Raw empirical observation artifact cannot be empty.", nameof(utf8Json));
        }

        return JsonSerializer.Deserialize<EmpiricalRawObservationArtifact>(utf8Json, JsonOptions)
            ?? throw new InvalidOperationException("Raw empirical observation artifact produced no document.");
    }

    public EmpiricalRawObservationArtifact Validate()
    {
        if (!StringComparer.Ordinal.Equals(FormatVersion, SupportedFormatVersion))
        {
            throw new InvalidOperationException($"Unsupported raw observation format '{FormatVersion}'.");
        }

        EmpiricalTreatmentDefinition.Require(CaseId, nameof(CaseId));
        EmpiricalTreatmentDefinition.Require(TreatmentId, nameof(TreatmentId));
        ArgumentNullException.ThrowIfNull(Measurements);
        ArgumentNullException.ThrowIfNull(FailedNonCompensableGates);
        if (RecordedAt == default)
        {
            throw new InvalidOperationException("Raw observation recorded-at timestamp must be present.");
        }

        if (Measurements.Any(item => string.IsNullOrWhiteSpace(item.Key) || !double.IsFinite(item.Value)))
        {
            throw new InvalidOperationException("Raw observation measurements must have non-empty ids and finite values.");
        }

        if (FailedNonCompensableGates.Any(string.IsNullOrWhiteSpace)
            || FailedNonCompensableGates.Distinct(StringComparer.Ordinal).Count() != FailedNonCompensableGates.Count)
        {
            throw new InvalidOperationException("Raw observation failed-gate ids must be non-empty and unique.");
        }

        return this;
    }

    public void RequireMatches(EmpiricalCaseObservation manifestObservation)
    {
        ArgumentNullException.ThrowIfNull(manifestObservation);

        if (!StringComparer.Ordinal.Equals(CaseId, manifestObservation.CaseId)
            || !StringComparer.Ordinal.Equals(TreatmentId, manifestObservation.TreatmentId)
            || Status != manifestObservation.Status)
        {
            throw new InvalidOperationException(
                $"Raw observation identity/status does not match manifest observation '{manifestObservation.CaseId}/{manifestObservation.TreatmentId}'.");
        }

        if (Measurements.Count != manifestObservation.Measurements.Count
            || Measurements.Any(item => !manifestObservation.Measurements.TryGetValue(item.Key, out var value) || value != item.Value))
        {
            throw new InvalidOperationException(
                $"Raw observation measurements do not match manifest observation '{CaseId}/{TreatmentId}'.");
        }

        var rawGates = FailedNonCompensableGates.OrderBy(item => item, StringComparer.Ordinal).ToArray();
        var manifestGates = manifestObservation.FailedNonCompensableGates.OrderBy(item => item, StringComparer.Ordinal).ToArray();
        if (!rawGates.SequenceEqual(manifestGates, StringComparer.Ordinal))
        {
            throw new InvalidOperationException(
                $"Raw observation failed-gate evidence does not match manifest observation '{CaseId}/{TreatmentId}'.");
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };
}
