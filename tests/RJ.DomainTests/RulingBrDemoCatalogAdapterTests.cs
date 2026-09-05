using RJ.BenchmarkCli;

namespace RJ.DomainTests;

public sealed class RulingBrDemoCatalogAdapterTests
{
    [Fact]
    public async Task BuildAsync_creates_external_catalog_from_read_only_rulingbr_archive()
    {
        var root = Path.Combine("C:\\Projetos\\RJ", "rulingbr");
        var info = await RulingBrDemoCatalogAdapter.BuildAsync(root, "abc123", CancellationToken.None);
        var external = RJ.Application.Benchmarking.ExternalGenerationBenchmarkCatalog.Parse(await File.ReadAllBytesAsync(info.CatalogPath));
        var catalog = external.ToBenchmarkCatalog();

        Assert.True(catalog.Cases.Count > 0);
        Assert.StartsWith("rulingbr-demo-", catalog.Version, StringComparison.Ordinal);
        Assert.Equal(root, info.CorpusRoot);
        Assert.Equal(64, info.CatalogSha256.Length);
        Assert.Equal(64, info.CorpusArtifactSha256.Length);
        Assert.Contains(Path.Combine(root, "rulingbr-v1.2.jsonl"), info.UsedArtifactPaths);
    }

    [Fact]
    public async Task ReadCasesFromFileAsync_enumerates_all_records_in_sample_and_archive_formats()
    {
        var sample = Path.Combine("C:\\Projetos\\RJ", "rulingbr", "sample-5.json");
        var sampleCases = await RulingBrDemoCatalogAdapter.ReadCasesFromFileAsync(sample, CancellationToken.None);
        Assert.Equal(5, sampleCases.Length);

        var archiveExtracted = await RulingBrDemoCatalogAdapter.ResolveCorpusPathAsync(Path.Combine("C:\\Projetos\\RJ", "rulingbr"), CancellationToken.None);
        var archiveCases = await RulingBrDemoCatalogAdapter.ReadCasesFromFileAsync(archiveExtracted, CancellationToken.None);
        Assert.Equal(10574, archiveCases.Length);
    }
}
