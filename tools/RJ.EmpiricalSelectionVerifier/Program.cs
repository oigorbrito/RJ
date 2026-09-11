using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using RJ.Application.Benchmarking;

if (args.Length != 4)
{
    Console.Error.WriteLine("Usage: RJ.EmpiricalSelectionVerifier <manifest.json> <expected-manifest-sha256> <artifact-root> <executed-git-commit>");
    return 2;
}

var manifestPath = Path.GetFullPath(args[0]);
var expectedManifestSha = args[1].Trim().ToLowerInvariant();
var artifactRoot = Path.GetFullPath(args[2]);
var executedGitCommit = args[3].Trim().ToLowerInvariant();
var policyJsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

if (!File.Exists(manifestPath) || !Directory.Exists(artifactRoot))
{
    Console.Error.WriteLine("Empirical selection manifest or artifact root is unavailable.");
    return 2;
}

try
{
    var manifestBytes = await File.ReadAllBytesAsync(manifestPath);
    var actualManifestSha = EmpiricalSelectionManifest.ComputeSha256(manifestBytes);
    if (!StringComparer.Ordinal.Equals(actualManifestSha, expectedManifestSha))
    {
        Console.Error.WriteLine($"Manifest SHA-256 mismatch: expected {expectedManifestSha}, observed {actualManifestSha}.");
        return 3;
    }

    var manifest = EmpiricalSelectionManifest.Parse(manifestBytes).Validate();
    if (!StringComparer.Ordinal.Equals(manifest.GitCommit.ToLowerInvariant(), executedGitCommit))
    {
        Console.Error.WriteLine($"Executed git commit mismatch: manifest {manifest.GitCommit}, observed {executedGitCommit}.");
        return 3;
    }

    var observedRuntime = RuntimeInformation.FrameworkDescription;
    if (!StringComparer.Ordinal.Equals(manifest.Runtime, observedRuntime))
    {
        Console.Error.WriteLine($"Runtime mismatch: manifest '{manifest.Runtime}', observed '{observedRuntime}'.");
        return 3;
    }

    var corpusBytes = await VerifyArtifactAsync(
        artifactRoot,
        manifest.CorpusManifestReference,
        manifest.CorpusManifestSha256,
        "corpus manifest");
    var corpus = Eval010CorpusManifest.Parse(corpusBytes);
    manifest.RequireMatchesCorpus(corpus);

    await VerifyArtifactAsync(
        artifactRoot,
        manifest.Baseline.ConfigurationReference,
        manifest.Baseline.ConfigurationSha256,
        $"baseline configuration {manifest.Baseline.TreatmentId}");
    await VerifyArtifactAsync(
        artifactRoot,
        manifest.Challenger.ConfigurationReference,
        manifest.Challenger.ConfigurationSha256,
        $"challenger configuration {manifest.Challenger.TreatmentId}");
    await VerifyArtifactAsync(
        artifactRoot,
        manifest.DependencyEvidenceReference,
        manifest.DependencyEvidenceSha256,
        "dependency evidence");
    await VerifyArtifactAsync(
        artifactRoot,
        manifest.CommandEvidenceReference,
        manifest.CommandEvidenceSha256,
        "command evidence");

    foreach (var observation in manifest.Observations)
    {
        var rawBytes = await VerifyArtifactAsync(
            artifactRoot,
            observation.ArtifactReference,
            observation.ArtifactSha256,
            $"observation {observation.CaseId}/{observation.TreatmentId}");
        var raw = EmpiricalRawObservationArtifact.Parse(rawBytes).Validate();
        raw.RequireMatches(observation);

        var sourceBytes = await VerifyArtifactAsync(
            artifactRoot,
            raw.SourceArtifactReference,
            raw.SourceArtifactSha256,
            $"source artifact for observation {observation.CaseId}/{observation.TreatmentId}");
        var policyBytes = await VerifyArtifactAsync(
            artifactRoot,
            raw.MaterializationPolicyReference,
            raw.MaterializationPolicySha256,
            $"materialization policy for observation {observation.CaseId}/{observation.TreatmentId}");

        var generationReport = GenerationBenchmarkJson.Parse(sourceBytes);
        var generationPolicy = JsonSerializer.Deserialize<GenerationEmpiricalObservationPolicy>(policyBytes, policyJsonOptions)
            ?? throw new InvalidOperationException("Generation materialization policy produced no document.");
        generationPolicy.Validate().RequireMatches(generationReport);
        if (!StringComparer.Ordinal.Equals(generationPolicy.TreatmentId, raw.TreatmentId))
        {
            throw new InvalidOperationException(
                $"Materialization policy treatment '{generationPolicy.TreatmentId}' does not match raw observation treatment '{raw.TreatmentId}'.");
        }

        var rematerialized = new GenerationEmpiricalObservationMaterializer().Materialize(
            generationReport,
            raw.SourceArtifactReference,
            raw.SourceArtifactSha256,
            raw.MaterializationPolicyReference,
            raw.MaterializationPolicySha256,
            raw.RecordedAt,
            generationPolicy);
        var expected = rematerialized.SingleOrDefault(item => StringComparer.Ordinal.Equals(item.CaseId, raw.CaseId))
            ?? throw new InvalidOperationException(
                $"Source generation report does not contain observation case '{raw.CaseId}'.");

        if (!rawBytes.AsSpan().SequenceEqual(expected.ArtifactUtf8Json))
        {
            throw new InvalidOperationException(
                $"Raw observation '{raw.CaseId}/{raw.TreatmentId}' is not the deterministic materialization of its source report and policy.");
        }
    }

    var report = new EmpiricalSelectionService().Compare(
        manifest.Baseline,
        manifest.Challenger,
        manifest.Metrics,
        manifest.Observations);

    var json = JsonSerializer.Serialize(report, new JsonSerializerOptions
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    });
    Console.WriteLine(json);

    if (report.Decision == EmpiricalSelectionDecision.Blocked)
    {
        return 2;
    }

    return 0;
}
catch (JsonException exception)
{
    Console.Error.WriteLine(exception.Message);
    return 3;
}
catch (InvalidOperationException exception)
{
    Console.Error.WriteLine(exception.Message);
    return 3;
}
catch (ArgumentException exception)
{
    Console.Error.WriteLine(exception.Message);
    return 3;
}
catch (IOException exception)
{
    Console.Error.WriteLine(exception.Message);
    return 3;
}
catch (UnauthorizedAccessException exception)
{
    Console.Error.WriteLine(exception.Message);
    return 3;
}

static async Task<byte[]> VerifyArtifactAsync(
    string artifactRoot,
    string artifactReference,
    string expectedSha256,
    string label)
{
    var path = ResolveUnderRoot(artifactRoot, artifactReference);
    if (!File.Exists(path))
    {
        throw new InvalidOperationException($"Required {label} artifact is missing: {artifactReference}.");
    }

    var bytes = await File.ReadAllBytesAsync(path);
    var actual = EmpiricalSelectionManifest.ComputeSha256(bytes);
    if (!StringComparer.Ordinal.Equals(actual, expectedSha256.ToLowerInvariant()))
    {
        throw new InvalidOperationException(
            $"{label} SHA-256 mismatch for '{artifactReference}': expected {expectedSha256.ToLowerInvariant()}, observed {actual}.");
    }

    return bytes;
}

static string ResolveUnderRoot(string root, string reference)
{
    if (string.IsNullOrWhiteSpace(reference) || Path.IsPathRooted(reference))
    {
        throw new InvalidOperationException("Artifact references must be non-empty relative paths.");
    }

    var rootFull = Path.GetFullPath(root);
    var relative = reference.Replace('/', Path.DirectorySeparatorChar);
    var candidate = Path.GetFullPath(Path.Combine(rootFull, relative));
    var rootWithSeparator = rootFull.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
        + Path.DirectorySeparatorChar;
    var comparison = OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;

    if (!candidate.StartsWith(rootWithSeparator, comparison))
    {
        throw new InvalidOperationException($"Artifact reference escapes the configured root: {reference}.");
    }

    return candidate;
}
