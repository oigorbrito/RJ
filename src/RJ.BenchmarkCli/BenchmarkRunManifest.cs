namespace RJ.BenchmarkCli;

public sealed record BenchmarkRunManifest(
    string GitCommit,
    string Runtime,
    string CatalogVersion,
    string ModelId,
    string ModelConfiguration,
    string Seed,
    string Command,
    string OutputPath,
    int ExitCode,
    bool Passed);
