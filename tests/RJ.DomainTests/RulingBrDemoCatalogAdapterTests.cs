using RJ.BenchmarkCli;

namespace RJ.DomainTests;

public sealed class RulingBrDemoCatalogAdapterTests
{
    [RealLegalCorpusFact]
    public async Task BuildAsync_creates_external_catalog_from_read_only_rulingbr_archive()
    {
        var root = Environment.GetEnvironmentVariable("RJ_REAL_RULINGBR_CORPUS")
            ?? throw new InvalidOperationException("RJ_REAL_RULINGBR_CORPUS is required for real-corpus tests.");
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

    [RealLegalCorpusFact]
    public async Task ReadCasesFromFileAsync_enumerates_all_records_in_sample_and_archive_formats()
    {
        var root = Environment.GetEnvironmentVariable("RJ_REAL_RULINGBR_CORPUS")
            ?? throw new InvalidOperationException("RJ_REAL_RULINGBR_CORPUS is required for real-corpus tests.");
        var sample = Path.Combine(root, "sample-5.json");
        var sampleCases = await RulingBrDemoCatalogAdapter.ReadCasesFromFileAsync(sample, CancellationToken.None);
        Assert.Equal(5, sampleCases.Length);

        var archiveExtracted = await RulingBrDemoCatalogAdapter.ResolveCorpusPathAsync(root, CancellationToken.None);
        var archiveCases = await RulingBrDemoCatalogAdapter.ReadCasesFromFileAsync(archiveExtracted, CancellationToken.None);
        Assert.Equal(10574, archiveCases.Length);
    }

    [Fact]
    public async Task BuildAsync_creates_catalog_from_read_only_synthetic_jsonl()
    {
        using var fixture = new LegalCorpusTestFixture();
        var artifact = Path.Combine(fixture.RulingBrRoot, "rulingbr-v1.2.jsonl");
        var original = await File.ReadAllBytesAsync(artifact);
        var info = await RulingBrDemoCatalogAdapter.BuildAsync(fixture.RulingBrRoot, "abc123", CancellationToken.None);
        try
        {
            var external = RJ.Application.Benchmarking.ExternalGenerationBenchmarkCatalog.Parse(await File.ReadAllBytesAsync(info.CatalogPath));
            Assert.Equal(2, external.ToBenchmarkCatalog().Cases.Count);
            Assert.Equal(original, await File.ReadAllBytesAsync(artifact));
            Assert.Contains(artifact, info.UsedArtifactPaths);
            Assert.Equal(64, info.CorpusArtifactSha256.Length);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(info.CatalogPath)!, recursive: true);
        }
    }

    [Fact]
    public async Task ReadCasesFromFileAsync_preserves_all_synthetic_array_and_jsonl_records()
    {
        using var fixture = new LegalCorpusTestFixture();
        var sample = await RulingBrDemoCatalogAdapter.ReadCasesFromFileAsync(Path.Combine(fixture.RulingBrRoot, "sample-5.json"), CancellationToken.None);
        var jsonl = await RulingBrDemoCatalogAdapter.ReadCasesFromFileAsync(Path.Combine(fixture.RulingBrRoot, "rulingbr-v1.2.jsonl"), CancellationToken.None);
        Assert.Equal(2, sample.Length);
        Assert.Equal(sample, jsonl);
        Assert.NotEqual(sample[0].SourceText, sample[1].SourceText);
    }
}
