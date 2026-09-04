namespace RJ.BenchmarkCli;

public sealed record CorpusAdmissionCliOptions(
    string CatalogPath,
    string CatalogSha256,
    string OutputPath)
{
    private static readonly string[] RequiredNames =
    [
        "--catalog",
        "--catalog-sha256",
        "--output"
    ];

    public static CorpusAdmissionCliOptions Parse(IReadOnlyList<string> args)
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

        var checksum = values["--catalog-sha256"].Trim().ToLowerInvariant();
        if (checksum.Length != 64 || checksum.Any(character => !Uri.IsHexDigit(character)))
        {
            throw new ArgumentException("--catalog-sha256 must contain exactly 64 hexadecimal characters.", nameof(args));
        }

        return new CorpusAdmissionCliOptions(
            values["--catalog"].Trim(),
            checksum,
            values["--output"].Trim());
    }
}
