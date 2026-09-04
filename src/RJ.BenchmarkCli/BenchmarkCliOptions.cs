using RJ.Application.Benchmarking;

namespace RJ.BenchmarkCli;

public sealed record BenchmarkCliOptions(
    string GitCommit,
    string Runtime,
    string ModelId,
    string ModelConfiguration,
    string Seed,
    string OutputPath,
    string? CatalogPath,
    string? CatalogSha256)
{
    private static readonly string[] RequiredNames =
    [
        "--git-commit",
        "--runtime",
        "--model-id",
        "--model-config",
        "--seed",
        "--output"
    ];

    private static readonly string[] OptionalNames =
    [
        "--catalog",
        "--catalog-sha256"
    ];

    public static BenchmarkCliOptions Parse(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);

        if (args.Count % 2 != 0)
        {
            throw new ArgumentException("Arguments must be provided as name/value pairs.", nameof(args));
        }

        var acceptedNames = RequiredNames.Concat(OptionalNames).ToArray();
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var index = 0; index < args.Count; index += 2)
        {
            var name = args[index];
            var value = args[index + 1];

            if (!acceptedNames.Contains(name, StringComparer.Ordinal))
            {
                throw new ArgumentException($"Unknown argument: {name}", nameof(args));
            }

            if (!values.TryAdd(name, value))
            {
                throw new ArgumentException($"Duplicate argument: {name}", nameof(args));
            }
        }

        foreach (var name in RequiredNames)
        {
            if (!values.TryGetValue(name, out var value) || string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException($"Required argument is missing or empty: {name}", nameof(args));
            }
        }

        var hasCatalog = values.TryGetValue("--catalog", out var catalogPath) && !string.IsNullOrWhiteSpace(catalogPath);
        var hasChecksum = values.TryGetValue("--catalog-sha256", out var catalogSha256) && !string.IsNullOrWhiteSpace(catalogSha256);
        if (hasCatalog != hasChecksum)
        {
            throw new ArgumentException("--catalog and --catalog-sha256 must be supplied together.", nameof(args));
        }

        if (hasChecksum && (catalogSha256!.Length != 64 || catalogSha256.Any(character => !Uri.IsHexDigit(character))))
        {
            throw new ArgumentException("--catalog-sha256 must contain exactly 64 hexadecimal characters.", nameof(args));
        }

        return new BenchmarkCliOptions(
            values["--git-commit"].Trim(),
            values["--runtime"].Trim(),
            values["--model-id"].Trim(),
            values["--model-config"].Trim(),
            values["--seed"].Trim(),
            values["--output"].Trim(),
            hasCatalog ? catalogPath!.Trim() : null,
            hasChecksum ? catalogSha256!.Trim().ToLowerInvariant() : null);
    }

    public GenerationBenchmarkMetadata ToMetadata(string catalogVersion) => new(
        GitCommit,
        Runtime,
        catalogVersion,
        ModelId,
        ModelConfiguration,
        Seed);
}
