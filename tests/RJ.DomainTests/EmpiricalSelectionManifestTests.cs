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
        Assert.Equal("configs/r0.json", parsed.Baseline.ConfigurationReference);
    }

    [Fact]
    public void Validate_rejects_undeclared_failed_gate()
    {
        var manifest = Manifest() with
        {
            Observations =
            [
                Observation("case-1", "r0", EmpiricalExecutionStatus.Fail, ["undeclared_gate"]),
                Observation("case-1", "r1")
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
            new EmpiricalTreatmentDefinition("r0", EmpiricalTreatmentKind.Retrieval, "configs/r0.json", "not-a-hash", "baseline"));

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

    [Fact]
    public void RequireMatchesCorpus_accepts_exact_30_case_paired_population()
    {
        var corpus = Corpus(30);
        var observations = corpus.Cases
            .SelectMany(item => new[] { Observation(item.CaseId, "r0"), Observation(item.CaseId, "r1") })
            .ToArray();
        var manifest = Manifest() with { Observations = observations };

        var result = manifest.Validate().RequireMatchesCorpus(corpus);

        Assert.Same(manifest, result);
        Assert.Equal(60, result.Observations.Count);
    }

    [Fact]
    public void RequireMatchesCorpus_rejects_silent_case_omission()
    {
        var corpus = Corpus(30);
        var observations = corpus.Cases
            .Take(29)
            .SelectMany(item => new[] { Observation(item.CaseId, "r0"), Observation(item.CaseId, "r1") })
            .ToArray();
        var manifest = Manifest() with { Observations = observations };

        var error = Assert.Throws<InvalidOperationException>(() =>
            manifest.Validate().RequireMatchesCorpus(corpus));

        Assert.Contains("exactly match", error.Message, StringComparison.OrdinalIgnoreCase);
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
            [Observation("case-1", "r0"), Observation("case-1", "r1")]);

    private static EmpiricalTreatmentDefinition Treatment(string id, EmpiricalTreatmentKind kind) =>
        new(id, kind, $"configs/{id}.json", Sha($"config:{id}"), $"Treatment {id}");

    private static EmpiricalCaseObservation Observation(
        string caseId,
        string treatmentId,
        EmpiricalExecutionStatus status = EmpiricalExecutionStatus.Pass,
        IReadOnlyList<string>? failedGates = null) =>
        new(
            caseId,
            treatmentId,
            status,
            new Dictionary<string, double>
            {
                ["quality"] = 0.8,
                ["latency_ms"] = 100
            },
            failedGates ?? [],
            $"raw/{caseId}-{treatmentId}.json",
            Sha($"raw:{caseId}:{treatmentId}"));

    private static Eval010CorpusManifest Corpus(int count)
    {
        var cases = Enumerable.Range(1, count)
            .Select(index => new Eval010CorpusCase(
                $"case-{index:00}",
                ValidCnj(index),
                $"source/case-{index:00}.json",
                Sha($"source:{index}"),
                $"oracle/case-{index:00}.json",
                Sha($"oracle:{index}"),
                $"review/case-{index:00}.json",
                Sha($"review:{index}"),
                $"author-{index:00}",
                $"reviewer-{index:00}",
                DateTimeOffset.Parse("2026-09-11T12:00:00-03:00")))
            .ToArray();

        return new Eval010CorpusManifest(
            Eval010CorpusManifest.SupportedFormatVersion,
            "test-corpus-v1",
            DateTimeOffset.Parse("2026-09-11T11:00:00-03:00"),
            cases);
    }

    private static string ValidCnj(int sequence)
    {
        var process = sequence.ToString("0000000", System.Globalization.CultureInfo.InvariantCulture);
        const string suffix = "20268160021";
        var baseDigits = process + suffix;
        var remainder = 0;
        foreach (var digit in baseDigits + "00")
        {
            remainder = ((remainder * 10) + digit - '0') % 97;
        }

        var checkDigits = 98 - remainder;
        return process
            + checkDigits.ToString("00", System.Globalization.CultureInfo.InvariantCulture)
            + suffix;
    }

    private static string Sha(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant();
    }
}
