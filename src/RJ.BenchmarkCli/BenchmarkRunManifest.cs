namespace RJ.BenchmarkCli;

public sealed record BenchmarkRunManifest(
    string ManifestVersion,
    string GitCommit,
    string Runtime,
    string CatalogVersion,
    string ModelId,
    string ModelConfiguration,
    string Seed,
    string Command,
    string OutputPath,
    string ReportSha256,
    int ExitCode,
    bool Passed);
