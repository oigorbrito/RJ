using System.Text;
using System.Text.Json;
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
                    "--report", report,
                    "--repo-commit", "abc123"
                ],
                CancellationToken.None);

            Assert.Equal(0, exit);
            var json = await File.ReadAllTextAsync(report);
            Assert.Contains("\"a\":", json, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("\"b\":", json, StringComparison.OrdinalIgnoreCase);

            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            Assert.Equal(1, root.GetProperty("a").GetProperty("totalCases").GetInt32());
            Assert.Equal(1, root.GetProperty("b").GetProperty("totalCases").GetInt32());
            Assert.Equal(0, root.GetProperty("a").GetProperty("retrievalCoverageCases").GetInt32());
            Assert.InRange(root.GetProperty("b").GetProperty("retrievalCoverageCases").GetInt32(), 0, 1);
            Assert.Equal("abc123", root.GetProperty("repositoryCommit").GetString());
            Assert.Equal(3, root.GetProperty("topK").GetInt32());
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
                "--report", report1
            ], CancellationToken.None);

            await OabRulingBrAbRunner.RunAsync([
                "--oab-root", oabRoot,
                "--rulingbr-root", rulingbrRoot,
                "--top-k", "1",
                "--report", report2
            ], CancellationToken.None);

            Assert.Equal(await File.ReadAllTextAsync(report1), await File.ReadAllTextAsync(report2));
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
}
