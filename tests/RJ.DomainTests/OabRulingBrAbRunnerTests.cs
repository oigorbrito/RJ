using System.Text;
using System.Text.Json;
using RJ.Application.Generation;
using RJ.Application.Retrieval;
using RJ.BenchmarkCli;

namespace RJ.DomainTests;

public sealed class OabRulingBrAbRunnerTests
{
    [Fact]
    public async Task RunAsync_writes_a_and_b_reports_and_uses_rulingbr_only_in_branch_b()
    {
        var oabRoot = await CreateOabFixtureAsync();
        var rulingbrRoot = await CreateRulingBrFixtureAsync();
        var report = Path.Combine(Path.GetTempPath(), $"rj-oab-rulingbr-ab-{Guid.NewGuid():N}.json");

        try
        {
            var exit = await OabRulingBrAbRunner.RunAsync(
                [
                    "--oab-root", oabRoot,
                    "--rulingbr-root", rulingbrRoot,
                    "--top-k", "3",
                    "--model-config", "local-lexical-v1",
                    "--report", report,
                    "--repo-commit", "abc123"
                ],
                CancellationToken.None);

            Assert.Equal(0, exit);
            var json = await File.ReadAllTextAsync(report);
            Assert.Contains("\"A\":", json, StringComparison.Ordinal);
            Assert.Contains("\"B\":", json, StringComparison.Ordinal);

            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            Assert.Equal(1, root.GetProperty("A").GetProperty("TotalCases").GetInt32());
            Assert.Equal(1, root.GetProperty("B").GetProperty("TotalCases").GetInt32());
            Assert.Equal(1, root.GetProperty("C").GetProperty("TotalCases").GetInt32());
            Assert.Equal(0, root.GetProperty("A").GetProperty("RetrievalCoverageCases").GetInt32());
            Assert.InRange(root.GetProperty("B").GetProperty("RetrievalCoverageCases").GetInt32(), 0, 1);
            Assert.InRange(root.GetProperty("C").GetProperty("RetrievalCoverageCases").GetInt32(), 0, 1);
            Assert.Equal("abc123", root.GetProperty("RepositoryCommit").GetString());
            Assert.Equal(3, root.GetProperty("TopK").GetInt32());
        }
        finally
        {
            Directory.Delete(oabRoot, recursive: true);
            Directory.Delete(rulingbrRoot, recursive: true);
            if (File.Exists(report))
            {
                File.Delete(report);
            }
        }
    }

    [Fact]
    public async Task RunAsync_is_deterministic_for_same_inputs()
    {
        var oabRoot = await CreateOabFixtureAsync();
        var rulingbrRoot = await CreateRulingBrFixtureAsync();
        var report1 = Path.Combine(Path.GetTempPath(), $"rj-oab-rulingbr-ab-{Guid.NewGuid():N}.json");
        var report2 = Path.Combine(Path.GetTempPath(), $"rj-oab-rulingbr-ab-{Guid.NewGuid():N}.json");

        try
        {
            await OabRulingBrAbRunner.RunAsync([
                "--oab-root", oabRoot,
                "--rulingbr-root", rulingbrRoot,
                "--top-k", "1",
                "--model-config", "local-lexical-v1",
                "--report", report1
            ], CancellationToken.None);

            await OabRulingBrAbRunner.RunAsync([
                "--oab-root", oabRoot,
                "--rulingbr-root", rulingbrRoot,
                "--top-k", "1",
                "--model-config", "local-lexical-v1",
                "--report", report2
            ], CancellationToken.None);

            Assert.Equal(
                NormalizeReportForDeterminism(await File.ReadAllTextAsync(report1)),
                NormalizeReportForDeterminism(await File.ReadAllTextAsync(report2)));
        }
        finally
        {
            Directory.Delete(oabRoot, recursive: true);
            Directory.Delete(rulingbrRoot, recursive: true);
            if (File.Exists(report1)) File.Delete(report1);
            if (File.Exists(report2)) File.Delete(report2);
        }
    }

    [Fact]
    public async Task RunAsync_fails_cleanly_when_rulingbr_corpus_is_missing()
    {
        var oabRoot = await CreateOabFixtureAsync();
        var report = Path.Combine(Path.GetTempPath(), $"rj-oab-rulingbr-ab-{Guid.NewGuid():N}.json");

        try
        {
            var exit = await OabRulingBrAbRunner.RunAsync([
                "--oab-root", oabRoot,
                "--rulingbr-root", Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}"),
                "--top-k", "1",
                "--model-config", "local-lexical-v1",
                "--report", report
            ], CancellationToken.None);

            Assert.Equal(2, exit);
            Assert.False(File.Exists(report));
        }
        finally
        {
            Directory.Delete(oabRoot, recursive: true);
            if (File.Exists(report)) File.Delete(report);
        }
    }

    [Fact]
    public async Task RunAsync_fails_cleanly_when_rulingbr_corpus_is_malformed()
    {
        var oabRoot = await CreateOabFixtureAsync();
        var rulingbrRoot = Path.Combine(Path.GetTempPath(), $"rj-rulingbr-{Guid.NewGuid():N}");
        Directory.CreateDirectory(rulingbrRoot);
        await File.WriteAllTextAsync(Path.Combine(rulingbrRoot, "sample-5.json"), "{not-json", Encoding.UTF8);
        var report = Path.Combine(Path.GetTempPath(), $"rj-oab-rulingbr-ab-{Guid.NewGuid():N}.json");

        try
        {
            var exit = await OabRulingBrAbRunner.RunAsync([
                "--oab-root", oabRoot,
                "--rulingbr-root", rulingbrRoot,
                "--top-k", "1",
                "--model-config", "local-lexical-v1",
                "--report", report
            ], CancellationToken.None);

            Assert.Equal(2, exit);
            Assert.False(File.Exists(report));
        }
        finally
        {
            Directory.Delete(oabRoot, recursive: true);
            Directory.Delete(rulingbrRoot, recursive: true);
            if (File.Exists(report)) File.Delete(report);
        }
    }

    [Fact]
    public async Task RunAsync_fails_cleanly_when_challenger_model_configuration_is_missing()
    {
        Assert.Throws<ArgumentException>(() => new OabRulingBrGenerationChallengerModel(" "));
    }

    [Fact]
    public async Task Challenger_uses_single_best_supported_claim_with_context_citation()
    {
        var model = new OabRulingBrGenerationChallengerModel("local-lexical-v1");
        var context = CreateContextWithEvidence([
            ("doc-a", "primeira evidencia sem termos relevantes", 1f),
            ("doc-b", "A tutela foi deferida com fundamento expresso.", 3f),
            ("doc-c", "outra evidencia acessoria", 2f)
        ]);

        var output = await model.GenerateAsync(context, CancellationToken.None);

        Assert.False(output.Abstained);
        Assert.Single(output.Claims);
        Assert.Single(output.Claims[0].Citations);
        Assert.Equal("doc-b", output.Claims[0].Citations[0].DocumentId);
    }

    [Fact]
    public async Task Challenger_rejects_empty_context()
    {
        var model = new OabRulingBrGenerationChallengerModel("local-lexical-v1");
        var context = new GenerationContext("case-1", "tutela", 100, 0, []);

        await Assert.ThrowsAsync<InvalidOperationException>(() => model.GenerateAsync(context, CancellationToken.None));
    }

    private static string NormalizeReportForDeterminism(string json)
    {
        var properties = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json)
            ?? throw new InvalidOperationException("AB report JSON could not be parsed.");
        properties.Remove("Runtime");
        return JsonSerializer.Serialize(properties);
    }

    private static async Task<string> CreateOabFixtureAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), $"rj-oab-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(root, "data", "oab_bench", "reference_answer"));
        await File.WriteAllTextAsync(Path.Combine(root, "data", "oab_bench", "question.jsonl"), """
        {"question_id":"q-1","category":"cat-1","statement":"QUESTÃO\n1. Qual é a resposta?"}
        """, Encoding.UTF8);
        await File.WriteAllTextAsync(Path.Combine(root, "data", "oab_bench", "reference_answer", "guidelines.jsonl"), """
        {"question_id":"q-1","choices":[{"turns":["Qual é a resposta?"]}]}
        """, Encoding.UTF8);
        await File.WriteAllTextAsync(Path.Combine(root, "data", "judge_prompts.jsonl"), """
        {"name":"single-v1","type":"single","system_prompt":"x","prompt_template":"y","description":"z","category":"general","output_format":"[[rating]]"}
        """, Encoding.UTF8);
        return root;
    }

    private static async Task<string> CreateRulingBrFixtureAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), $"rj-rulingbr-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        await File.WriteAllTextAsync(Path.Combine(root, "sample-5.json"), """
        {"ementa":"decisão de exemplo","acordao":"acórdão de exemplo","relatorio":"relatório de exemplo","voto":"voto de exemplo","area":"direito civil","relator":"relator exemplo"}
        """, Encoding.UTF8);
        return root;
    }

    private static GenerationContext CreateContextWithEvidence(
        IReadOnlyList<(string DocumentId, string Excerpt, float Rank)> evidence)
    {
        var items = evidence.Select(item =>
        {
            var position = SourcePosition.Create(0, item.Excerpt.Length, item.Excerpt.Length);
            return new GenerationContextItem(
                "case-1",
                item.DocumentId,
                "source.txt",
                new string('a', 64),
                item.Excerpt,
                position,
                item.Rank);
        }).ToArray();

        return new GenerationContext("case-1", "A tutela foi deferida?", 1000, items.Sum(item => item.Excerpt.Length), items);
    }
}
