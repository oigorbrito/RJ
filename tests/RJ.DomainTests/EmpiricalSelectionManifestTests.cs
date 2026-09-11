using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using RJ.Application.Benchmarking;

namespace RJ.DomainTests;

public sealed class EmpiricalSelectionManifestTests
{
    [Fact]
    public void Validate_accepts_frozen_paired_manifest()
    {
        var manifest = Manifest();

        var validated = manifest.Validate();

        Assert.Same(manifest, validated);
        Assert.Equal(EmpiricalTreatmentKind.Retrieval, validated.Baseline.Kind);
        Assert.Equal(2, validated.Observations.Count);
    }

    [Fact]
    public void Parse_round_trips_string_enums_and_hashes()
    {
        var manifest = Manifest();
        var bytes = JsonSerializer.SerializeToUtf8Bytes(manifest, new JsonSerializerOptions
        {
            Converters = { new JsonStringEnumConverter() }
        });

        var parsed = EmpiricalSelectionManifest.Parse(bytes).Validate();

        Assert.Equal(EmpiricalMetricDirection.HigherIsBetter, parsed.Metrics[0].Direction);
        Assert.Equal(EmpiricalExecutionStatus.Pass, parsed.Observations[0].Status);
        Assert.Equal(manifest.CorpusManifestSha256, parsed.CorpusManifestSha256);
    }

    [Fact]
    public void Validate_rejects_undeclared_failed_gate()
    {
        var manifest = Manifest() with
        {
            Observations =
            [
                Observation("r0", EmpiricalExecutionStatus.Fail, ["undeclared_gate"]),
                Observation("r1")
            ]
        };

        var error = Assert.Throws<InvalidOperationException>(manifest.Validate);
        Assert.Contains("undeclared", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_rejects_cross_kind_comparison()
    {
        var manifest = Manifest() with
        {
            Challenger = Treatment("g1", EmpiricalTreatmentKind.Generation)
        };

        var error = Assert.Throws<InvalidOperationException>(manifest.Validate);
        Assert.Contains("same treatment kind", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Treatment_and_observation_require_exact_sha256()
    {
        Assert.Throws<ArgumentException>(() =>
            new EmpiricalTreatmentDefinition("r0", EmpiricalTreatmentKind.Retrieval, "not-a-hash", "baseline"));

        Assert.Throws<ArgumentException>(() =>
            new EmpiricalCaseObservation(
                "case-1",
                "r0",
                EmpiricalExecutionStatus.Pass,
                new Dictionary<string, double> { ["quality"] = 1 },
                [],
                "raw/case-1-r0.json",
                "bad"));
    }

    private static EmpiricalSelectionManifest Manifest() =>
        new(
            EmpiricalSelectionManifest.SupportedFormatVersion,
            "Does the challenger dominate the baseline under paired admitted evidence?",
            "corpus/eval010-manifest.json",
            Sha("corpus"),
            new string('a', 40),
            ".NET 10.0.0",
            "run/dependencies.txt",
            Sha("dependencies"),
            "run/commands.txt",
            Sha("commands"),
            DateTimeOffset.Parse("2026-09-11T12:00:00-03:00"),
            Treatment("r0", EmpiricalTreatmentKind.Retrieval),
            Treatment("r1", EmpiricalTreatmentKind.Retrieval),
            [
                new EmpiricalMetricDefinition("quality", EmpiricalMetricDirection.HigherIsBetter, true),
                new EmpiricalMetricDefinition("latency_ms", EmpiricalMetricDirection.LowerIsBetter, true)
            ],
            ["no_oracle_leakage", "no_raw_pii"],
            [Observation("r0"), Observation("r1")]);

    private static EmpiricalTreatmentDefinition Treatment(string id, EmpiricalTreatmentKind kind) =>
        new(id, kind, Sha($"config:{id}"), $"Treatment {id}");

    private static EmpiricalCaseObservation Observation(
        string treatmentId,
        EmpiricalExecutionStatus status = EmpiricalExecutionStatus.Pass,
        IReadOnlyList<string>? failedGates = null) =>
        new(
            "case-1",
            treatmentId,
            status,
            new Dictionary<string, double>
            {
                ["quality"] = 0.8,
                ["latency_ms"] = 100
            },
            failedGates ?? [],
            $"raw/case-1-{treatmentId}.json",
            Sha($"raw:{treatmentId}"));

    private static string Sha(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant();
    }
}
