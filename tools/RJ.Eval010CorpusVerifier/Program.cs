using System.Text.Json;
using RJ.Application.Benchmarking;

if (args.Length is < 3 or > 4)
{
    Console.Error.WriteLine("Usage: RJ.Eval010CorpusVerifier <manifest.json> <manifest-sha256> <artifact-root> [benchmark-catalog.json]");
    return 2;
}

string manifestPath;
string expectedManifestSha256;
string artifactRoot;
string? catalogPath;
try
{
    manifestPath = Path.GetFullPath(args[0]);
    expectedManifestSha256 = NormalizeSha256(args[1]);
    artifactRoot = Path.GetFullPath(args[2]);
    catalogPath = args.Length == 4 ? Path.GetFullPath(args[3]) : null;
}
catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
{
    Console.Error.WriteLine($"EVAL-010 verifier precondition failed: {exception.Message}");
    return 2;
}

if (!File.Exists(manifestPath))
{
    Console.Error.WriteLine($"Manifest not found: {manifestPath}");
    return 2;
}

if (!Directory.Exists(artifactRoot))
{
    Console.Error.WriteLine($"Artifact root not found: {artifactRoot}");
    return 2;
}

if (catalogPath is not null && !File.Exists(catalogPath))
{
    Console.Error.WriteLine($"Benchmark catalog not found: {catalogPath}");
    return 2;
}

try
{
    var manifestBytes = await File.ReadAllBytesAsync(manifestPath);
    var actualManifestSha256 = ExternalGenerationBenchmarkCatalog.ComputeSha256(manifestBytes);
    if (!StringComparer.Ordinal.Equals(actualManifestSha256, expectedManifestSha256))
    {
        throw new InvalidOperationException(
            $"Frozen manifest SHA-256 mismatch. Expected {expectedManifestSha256}, observed {actualManifestSha256}.");
    }

    var manifest = Eval010CorpusManifest.Parse(manifestBytes);
    ExternalGenerationBenchmarkCatalog? catalog = null;
    if (catalogPath is not null)
    {
        catalog = ExternalGenerationBenchmarkCatalog.Parse(await File.ReadAllBytesAsync(catalogPath));
    }

    var service = new Eval010CorpusAdmissionService(new RootedArtifactReader(artifactRoot));
    var report = await service.AdmitAsync(manifest, catalog, CancellationToken.None);
    Console.WriteLine(JsonSerializer.Serialize(
        new Eval010VerificationEnvelope(actualManifestSha256, report),
        new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        }));
    return report.Passed ? 0 : 3;
}
catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or JsonException or IOException or UnauthorizedAccessException)
{
    Console.Error.WriteLine($"EVAL-010 corpus verification failed: {exception.Message}");
    return 3;
}

static string NormalizeSha256(string value)
{
    var normalized = value?.Trim().ToLowerInvariant();
    if (string.IsNullOrWhiteSpace(normalized)
        || normalized.Length != 64
        || normalized.Any(character => !Uri.IsHexDigit(character)))
    {
        throw new ArgumentException("manifest-sha256 must contain exactly 64 hexadecimal characters.", nameof(value));
    }

    return normalized;
}

sealed record Eval010VerificationEnvelope(
    string ManifestSha256,
    Eval010CorpusAdmissionReport Admission);

sealed class RootedArtifactReader(string root) : IBenchmarkArtifactReader
{
    private readonly string _root = EnsureTrailingSeparator(Path.GetFullPath(root));

    public async Task<ReadOnlyMemory<byte>> ReadAsync(string artifactReference, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(artifactReference))
        {
            throw new ArgumentException("Artifact reference cannot be empty.", nameof(artifactReference));
        }

        var candidate = Path.GetFullPath(Path.Combine(_root, artifactReference));
        if (!candidate.StartsWith(_root, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Artifact reference escapes the configured corpus root.");
        }

        return await File.ReadAllBytesAsync(candidate, cancellationToken);
    }

    private static string EnsureTrailingSeparator(string value) =>
        value.EndsWith(Path.DirectorySeparatorChar)
            ? value
            : value + Path.DirectorySeparatorChar;
}
