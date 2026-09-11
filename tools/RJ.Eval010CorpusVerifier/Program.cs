using System.Text.Json;
using RJ.Application.Benchmarking;

if (args.Length is < 2 or > 3)
{
    Console.Error.WriteLine("Usage: RJ.Eval010CorpusVerifier <manifest.json> <artifact-root> [benchmark-catalog.json]");
    return 2;
}

var manifestPath = Path.GetFullPath(args[0]);
var artifactRoot = Path.GetFullPath(args[1]);
var catalogPath = args.Length == 3 ? Path.GetFullPath(args[2]) : null;

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
    var manifest = Eval010CorpusManifest.Parse(manifestBytes);
    ExternalGenerationBenchmarkCatalog? catalog = null;
    if (catalogPath is not null)
    {
        catalog = ExternalGenerationBenchmarkCatalog.Parse(await File.ReadAllBytesAsync(catalogPath));
    }

    var service = new Eval010CorpusAdmissionService(new RootedArtifactReader(artifactRoot));
    var report = await service.AdmitAsync(manifest, catalog, CancellationToken.None);
    Console.WriteLine(JsonSerializer.Serialize(report, new JsonSerializerOptions
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
