using System.Text.Json;
using System.Text.Json.Serialization;
using RJ.Application.Benchmarking;

if (args.Length != 3)
{
    Console.Error.WriteLine("Usage: RJ.EmpiricalSelectionVerifier <manifest.json> <expected-manifest-sha256> <artifact-root>");
    return 2;
}

var manifestPath = Path.GetFullPath(args[0]);
var expectedManifestSha = args[1].Trim().ToLowerInvariant();
var artifactRoot = Path.GetFullPath(args[2]);

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

    await VerifyArtifactAsync(
        artifactRoot,
        manifest.CorpusManifestReference,
        manifest.CorpusManifestSha256,
        "corpus manifest");
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
        await VerifyArtifactAsync(
            artifactRoot,
            observation.ArtifactReference,
            observation.ArtifactSha256,
            $"observation {observation.CaseId}/{observation.TreatmentId}");
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

static async Task VerifyArtifactAsync(
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
