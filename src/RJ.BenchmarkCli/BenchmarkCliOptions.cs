using RJ.Application.Benchmarking;

namespace RJ.BenchmarkCli;

public sealed record BenchmarkCliOptions(
    string GitCommit,
    string Runtime,
    string ModelId,
    string ModelConfiguration,
    string Seed,
    string OutputPath)
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

    public static BenchmarkCliOptions Parse(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);

        if (args.Count % 2 != 0)
        {
            throw new ArgumentException("Arguments must be provided as name/value pairs.", nameof(args));
        }

        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var index = 0; index < args.Count; index += 2)
        {
            var name = args[index];
            var value = args[index + 1];

            if (!RequiredNames.Contains(name, StringComparer.Ordinal))
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

        return new BenchmarkCliOptions(
            values["--git-commit"].Trim(),
            values["--runtime"].Trim(),
            values["--model-id"].Trim(),
            values["--model-config"].Trim(),
            values["--seed"].Trim(),
            values["--output"].Trim());
    }

    public GenerationBenchmarkMetadata ToMetadata() => new(
        GitCommit,
        Runtime,
        ApprovedGenerationBenchmarkCatalog.Version,
        ModelId,
        ModelConfiguration,
        Seed);
}
