using RJ.Application.Benchmarking;

namespace RJ.BenchmarkCli;

public sealed class LocalBenchmarkArtifactReader(string catalogPath) : IBenchmarkArtifactReader
{
    private readonly string _baseDirectory = GetCatalogDirectory(catalogPath);

    public async Task<ReadOnlyMemory<byte>> ReadAsync(
        string artifactReference,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(artifactReference))
        {
            throw new ArgumentException("Artifact reference cannot be empty.", nameof(artifactReference));
        }

        if (Path.IsPathRooted(artifactReference))
        {
            throw new InvalidOperationException("Artifact reference must be relative to the catalog directory.");
        }

        var candidate = Path.GetFullPath(Path.Combine(_baseDirectory, artifactReference));
        var relative = Path.GetRelativePath(_baseDirectory, candidate);
        if (relative.Equals("..", StringComparison.Ordinal)
            || relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
            || Path.IsPathRooted(relative))
        {
            throw new InvalidOperationException("Artifact reference escapes the catalog directory.");
        }

        return await File.ReadAllBytesAsync(candidate, cancellationToken);
    }

    private static string GetCatalogDirectory(string catalogPath)
    {
        if (string.IsNullOrWhiteSpace(catalogPath))
        {
            throw new ArgumentException("Catalog path cannot be empty.", nameof(catalogPath));
        }

        var fullCatalogPath = Path.GetFullPath(catalogPath);
        return Path.GetDirectoryName(fullCatalogPath)
            ?? throw new InvalidOperationException("Catalog path must have a containing directory.");
    }
}
