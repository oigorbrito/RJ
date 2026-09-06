using System.Text.Json;
using System.Text.Json.Serialization;
using RJ.Application.Rag;

namespace RJ.DomainTests;

public sealed class LocalRagEvaluationTests
{
    private static readonly string RepoRoot = FindRepoRoot();
    private static readonly string FixturePath = Path.Combine(RepoRoot, "tests", "fixtures", "rj", "response_60031603620268160021_1.json");
    private static readonly string QuerySetPath = Path.Combine(RepoRoot, "docs", "onda1", "rj-query-set.v2.json");
    private static readonly JsonSerializerOptions SnapshotSerializerOptions = new() { WriteIndented = true };

    [Fact]
    public void Chunker_builds_deterministic_chunks_without_iaSummary()
    {
        using var fixture = JsonDocument.Parse(File.ReadAllText(FixturePath));
        var chunker = new LocalRagChunker();

        var first = chunker.BuildChunks(fixture, FixturePath);
        var second = chunker.BuildChunks(fixture, FixturePath);

        Assert.Equal(first.Select(item => item.ChunkId), second.Select(item => item.ChunkId));
        Assert.All(first, chunk => Assert.DoesNotContain("iaSummary", chunk.Content, StringComparison.OrdinalIgnoreCase));
        Assert.Contains(first, chunk => chunk.Section == "overview");
        Assert.Contains(first, chunk => chunk.Section == "steps");
    }

    [Fact]
    public void Evaluation_returns_reproducible_metrics_and_records_provenance()
    {
        using var fixture = JsonDocument.Parse(File.ReadAllText(FixturePath));
        using var querySet = JsonDocument.Parse(File.ReadAllText(QuerySetPath));
        var evaluator = new LocalRagEvaluator(new LocalRagChunker());

        var first = evaluator.Evaluate(fixture, querySet, FixturePath, "baseline-lexical", 5);
        var second = evaluator.Evaluate(fixture, querySet, FixturePath, "baseline-lexical", 5);

        Assert.Equal(first.Strategy, second.Strategy);
        Assert.Equal(first.QueryCount, second.QueryCount);
        Assert.Equal(first.HitAt1, second.HitAt1);
        Assert.Equal(first.HitAt3, second.HitAt3);
        Assert.Equal(first.HitAt5, second.HitAt5);
        Assert.Equal(first.Mrr, second.Mrr);
        Assert.Equal(first.PerQueryResults.Select(item => item.Status), second.PerQueryResults.Select(item => item.Status));
        Assert.All(first.PerQueryResults, item => Assert.Equal("HIT", item.Status));
        Assert.Contains(first.PerQueryResults, item => item.MatchedSource is not null);
    }

    [Fact]
    public void Search_can_find_exact_identifiers_in_the_local_index()
    {
        using var fixture = JsonDocument.Parse(File.ReadAllText(FixturePath));
        var index = new LocalRagIndex(new LocalRagChunker().BuildChunks(fixture, FixturePath));

        var cnj = index.Search("6003160-36.2026.8.16.0021", "6003160-36.2026.8.16.0021", 3);
        var oab = index.Search("6003160-36.2026.8.16.0021", "PR0035553", 3);

        Assert.NotEmpty(cnj);
        Assert.NotEmpty(oab);
        Assert.Contains(cnj, chunk => chunk.Content.Contains("6003160-36.2026.8.16.0021", StringComparison.OrdinalIgnoreCase) || chunk.Metadata.Values.Any(value => value.Contains("6003160-36.2026.8.16.0021", StringComparison.OrdinalIgnoreCase)));
        Assert.Contains(oab, chunk => chunk.Content.Contains("PR0035553", StringComparison.OrdinalIgnoreCase) || chunk.Metadata.Values.Any(value => value.Contains("PR0035553", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public void Write_reproducible_evaluation_snapshot()
    {
        using var fixture = JsonDocument.Parse(File.ReadAllText(FixturePath));
        using var querySet = JsonDocument.Parse(File.ReadAllText(QuerySetPath));
        var evaluator = new LocalRagEvaluator(new LocalRagChunker());

        var result = evaluator.Evaluate(fixture, querySet, FixturePath, "baseline-lexical", 5);
        var outputPath = Path.Combine(RepoRoot, "docs", "onda3", "rag-eval.v1.json");
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

        var snapshot = new
        {
            rag_eval_version = "rj-rag-eval-v1",
            dataset_version = "rj-juridical-response-60031603620268160021-2026-09-06",
            query_set_version = "rj-juridical-response-query-set-v2",
            chunk_schema_version = "rj-local-rag-chunk-v1",
            retriever = result.Strategy,
            query_count = result.QueryCount,
            hit_at_1 = result.HitAt1,
            hit_at_3 = result.HitAt3,
            hit_at_5 = result.HitAt5,
            mrr = result.Mrr,
            duration_ms = result.Duration.TotalMilliseconds,
            per_query_results = result.PerQueryResults
        };

        File.WriteAllText(outputPath, JsonSerializer.Serialize(snapshot, SnapshotSerializerOptions));
        Assert.True(File.Exists(outputPath));
    }

    private static string FindRepoRoot()
    {
        var current = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(current, "RJ.slnx")))
        {
            var parent = Directory.GetParent(current) ?? throw new InvalidOperationException("Repository root not found.");
            current = parent.FullName;
        }

        return current;
    }
}
