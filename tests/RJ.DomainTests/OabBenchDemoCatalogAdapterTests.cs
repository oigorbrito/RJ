using System.Text;
using RJ.Application.Generation;
using RJ.BenchmarkCli;

namespace RJ.DomainTests;

public sealed class OabBenchDemoCatalogAdapterTests
{
    [Fact]
    public async Task Demo_target_builds_structured_claim_without_accessing_oracle()
    {
        var model = new OabBenchDemoGenerationModel();
        var context = new GenerationContext(
            "case-1",
            "QUESTÃO\nQual é a resposta?",
            100,
            20,
            [
                new GenerationContextItem(
                    "case-1",
                    "doc-1",
                    "source.txt",
                    new string('a', 64),
                    "Fonte demo",
                    RJ.Application.Retrieval.SourcePosition.Create(0, 10, 10),
                    1.0f)
            ]);

        var output = await model.GenerateAsync(context, CancellationToken.None);

        Assert.False(output.Abstained);
        Assert.Single(output.Claims);
        Assert.Single(output.Claims[0].Citations);
        Assert.Equal("QUESTÃO", output.Claims[0].Text);
    }

    [Fact]
    public async Task BuildAsync_can_map_fixture_oab_bench_corpus_into_rj_catalog()
    {
        var root = await CreateFixtureAsync();
        try
        {
            var info = await OabBenchDemoCatalogAdapter.BuildAsync(root, "abc123", CancellationToken.None);
            var external = RJ.Application.Benchmarking.ExternalGenerationBenchmarkCatalog.Parse(await File.ReadAllBytesAsync(info.CatalogPath));
            var catalog = external.ToBenchmarkCatalog();

            Assert.True(catalog.Cases.Count > 0);
            Assert.StartsWith("oab-bench-demo-", catalog.Version, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task BuildAsync_creates_external_catalog_from_oab_bench_jsonl()
    {
        var root = await CreateFixtureAsync();

        try
        {
            var info = await OabBenchDemoCatalogAdapter.BuildAsync(root, "abc123", CancellationToken.None);

            Assert.True(File.Exists(info.CatalogPath));
            Assert.Equal(root, info.CorpusRoot);
            Assert.Equal(64, info.CatalogSha256.Length);
            Assert.Contains(Path.Combine(root, "data", "oab_bench", "question.jsonl"), info.UsedArtifactPaths);
            Assert.Contains(Path.Combine(root, "data", "oab_bench", "reference_answer", "guidelines.jsonl"), info.UsedArtifactPaths);
            Assert.Contains(Path.Combine(root, "data", "judge_prompts.jsonl"), info.UsedArtifactPaths);
            Assert.DoesNotContain(info.UsedArtifactPaths, path => path.Contains("model_answer", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static async Task<string> CreateFixtureAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), $"rj-oab-bench-fixture-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(root, "data", "oab_bench", "reference_answer"));
        Directory.CreateDirectory(Path.Combine(root, "data", "oab_bench", "model_answer"));
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
        return root;
    }
}
