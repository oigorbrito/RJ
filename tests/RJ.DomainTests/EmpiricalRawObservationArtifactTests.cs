using System.Text.Json;
using System.Text.Json.Serialization;
using RJ.Application.Benchmarking;

namespace RJ.DomainTests;

public sealed class EmpiricalRawObservationArtifactTests
{
    [Fact]
    public void RequireMatches_accepts_exact_manifest_observation()
    {
        var manifest = Observation();
        var raw = Raw();

        raw.Validate().RequireMatches(manifest);
    }

    [Fact]
    public void RequireMatches_rejects_measurement_transcription_drift()
    {
        var manifest = Observation();
        var raw = new EmpiricalRawObservationArtifact(
            EmpiricalRawObservationArtifact.SupportedFormatVersion,
            "case-1",
            "r1",
            EmpiricalExecutionStatus.Pass,
            new Dictionary<string, double>
            {
                ["quality"] = 0.81,
                ["latency_ms"] = 100
            },
            [],
            DateTimeOffset.Parse("2026-09-11T12:00:00-03:00"),
            "reports/r1-generation.json",
            new string('b', 64));

        var error = Assert.Throws<InvalidOperationException>(() => raw.Validate().RequireMatches(manifest));
        Assert.Contains("measurements", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Parse_preserves_string_execution_status_and_source_provenance()
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(Raw(), new JsonSerializerOptions
        {
            Converters = { new JsonStringEnumConverter() }
        });

        var parsed = EmpiricalRawObservationArtifact.Parse(bytes).Validate();

        Assert.Equal(EmpiricalExecutionStatus.Pass, parsed.Status);
        Assert.Equal("case-1", parsed.CaseId);
        Assert.Equal("r1", parsed.TreatmentId);
        Assert.Equal("reports/r1-generation.json", parsed.SourceArtifactReference);
        Assert.Equal(new string('b', 64), parsed.SourceArtifactSha256);
    }

    [Fact]
    public void Validate_rejects_missing_or_invalid_source_provenance()
    {
        Assert.Throws<ArgumentException>(() =>
            new EmpiricalRawObservationArtifact(
                EmpiricalRawObservationArtifact.SupportedFormatVersion,
                "case-1",
                "r1",
                EmpiricalExecutionStatus.Pass,
                new Dictionary<string, double> { ["quality"] = 1.0 },
                [],
                DateTimeOffset.Parse("2026-09-11T12:00:00-03:00"),
                "reports/r1-generation.json",
                "bad-hash"));
    }

    private static EmpiricalCaseObservation Observation() =>
        new(
            "case-1",
            "r1",
            EmpiricalExecutionStatus.Pass,
            new Dictionary<string, double>
            {
                ["quality"] = 0.8,
                ["latency_ms"] = 100
            },
            [],
            "raw/case-1-r1.json",
            new string('a', 64));

    private static EmpiricalRawObservationArtifact Raw() =>
        new(
            EmpiricalRawObservationArtifact.SupportedFormatVersion,
            "case-1",
            "r1",
            EmpiricalExecutionStatus.Pass,
            new Dictionary<string, double>
            {
                ["quality"] = 0.8,
                ["latency_ms"] = 100
            },
            [],
            DateTimeOffset.Parse("2026-09-11T12:00:00-03:00"),
            "reports/r1-generation.json",
            new string('b', 64));
}
