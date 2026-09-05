using System.Text;
using RJ.BenchmarkCli;

namespace RJ.DomainTests;

public sealed class OabBenchDemoCatalogAdapterTests
{
    [Fact]
    public async Task BuildAsync_can_map_real_oab_bench_corpus_into_rj_catalog()
    {
        var root = Path.Combine("C:\\Projetos\\RJ", "oab-bench");
        var info = await OabBenchDemoCatalogAdapter.BuildAsync(root, "abc123", CancellationToken.None);
        var external = RJ.Application.Benchmarking.ExternalGenerationBenchmarkCatalog.Parse(await File.ReadAllBytesAsync(info.CatalogPath));
        var catalog = external.ToBenchmarkCatalog();

        Assert.True(catalog.Cases.Count > 0);
        Assert.StartsWith("oab-bench-demo-", catalog.Version, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BuildAsync_creates_external_catalog_from_oab_bench_jsonl()
    {
        var root = Path.Combine(Path.GetTempPath(), $"rj-oab-bench-fixture-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(root, "data", "oab_bench", "reference_answer"));
        Directory.CreateDirectory(Path.Combine(root, "data", "oab_bench", "model_answer"));

        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(root, "data", "oab_bench", "question.jsonl"),
                """
                {"question_id":"q-1","category":"cat-1","statement":"Pergunta demo"}
                """,
                Encoding.UTF8);
            await File.WriteAllTextAsync(
                Path.Combine(root, "data", "oab_bench", "reference_answer", "guidelines.jsonl"),
                """
                {"question_id":"q-1","choices":[{"turns":["Resposta demo"]}]}
                """,
                Encoding.UTF8);
            await File.WriteAllTextAsync(
                Path.Combine(root, "data", "judge_prompts.jsonl"),
                """
                {"name":"single-v1","type":"single","system_prompt":"x","prompt_template":"y","description":"z","category":"general","output_format":"[[rating]]"}
                """,
                Encoding.UTF8);

            var info = await OabBenchDemoCatalogAdapter.BuildAsync(root, "abc123", CancellationToken.None);

            Assert.True(File.Exists(info.CatalogPath));
            Assert.Equal(root, info.CorpusRoot);
            Assert.Equal(64, info.CatalogSha256.Length);
            Assert.Contains(Path.Combine(root, "data", "oab_bench", "question.jsonl"), info.UsedArtifactPaths);
            Assert.Contains(Path.Combine(root, "data", "oab_bench", "reference_answer", "guidelines.jsonl"), info.UsedArtifactPaths);
            Assert.Contains(Path.Combine(root, "data", "judge_prompts.jsonl"), info.UsedArtifactPaths);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
