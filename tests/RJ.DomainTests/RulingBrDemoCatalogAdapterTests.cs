using System.Text;
using RJ.BenchmarkCli;

namespace RJ.DomainTests;

public sealed class RulingBrDemoCatalogAdapterTests
{
    [Fact]
    public async Task BuildAsync_creates_external_catalog_from_fixture_rulingbr_archive()
    {
        var root = await CreateFixtureAsync();
        try
        {
            var info = await RulingBrDemoCatalogAdapter.BuildAsync(root, "abc123", CancellationToken.None);
            var external = RJ.Application.Benchmarking.ExternalGenerationBenchmarkCatalog.Parse(await File.ReadAllBytesAsync(info.CatalogPath));
            var catalog = external.ToBenchmarkCatalog();

            Assert.True(catalog.Cases.Count > 0);
            Assert.StartsWith("rulingbr-demo-", catalog.Version, StringComparison.Ordinal);
            Assert.Equal(Path.GetFullPath(root), info.CorpusRoot);
            Assert.Equal(64, info.CatalogSha256.Length);
            Assert.Equal(64, info.CorpusArtifactSha256.Length);
            Assert.Contains(Path.Combine(Path.GetFullPath(root), "rulingbr-v1.2.jsonl"), info.UsedArtifactPaths);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ReadCasesFromFileAsync_enumerates_sample_and_jsonl_formats()
    {
        var root = await CreateFixtureAsync();
        try
        {
            var sample = Path.Combine(root, "sample-5.json");
            var sampleCases = await RulingBrDemoCatalogAdapter.ReadCasesFromFileAsync(sample, CancellationToken.None);
            Assert.Equal(2, sampleCases.Length);

            var archiveExtracted = await RulingBrDemoCatalogAdapter.ResolveCorpusPathAsync(root, CancellationToken.None);
            var archiveCases = await RulingBrDemoCatalogAdapter.ReadCasesFromFileAsync(archiveExtracted, CancellationToken.None);
            Assert.Equal(2, archiveCases.Length);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static async Task<string> CreateFixtureAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), $"rj-rulingbr-fixture-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        const string first = "{\"ementa\":\"decisão um\",\"acordao\":\"acórdão um\",\"area\":\"civil\",\"relator\":\"relator um\"}";
        const string second = "{\"ementa\":\"decisão dois\",\"acordao\":\"acórdão dois\",\"area\":\"trabalho\",\"relator\":\"relator dois\"}";
        await File.WriteAllTextAsync(Path.Combine(root, "rulingbr-v1.2.jsonl"), first + Environment.NewLine + second, Encoding.UTF8);
        await File.WriteAllTextAsync(Path.Combine(root, "sample-5.json"), "[" + first + "," + second + "]", Encoding.UTF8);
        return root;
    }
}
