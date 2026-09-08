using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Nodes;
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
    public void Chunker_ignores_unknown_fields_and_is_stable_under_property_reordering()
    {
        using var fixture = JsonDocument.Parse(File.ReadAllText(FixturePath));
        using var reordered = JsonDocument.Parse(CreateReorderedFixtureJson());

        var chunker = new LocalRagChunker();
        var original = chunker.BuildChunks(fixture, FixturePath);
        var variant = chunker.BuildChunks(reordered, FixturePath);

        Assert.Equal(original.Select(item => item.ChunkId), variant.Select(item => item.ChunkId));
        Assert.Equal(original.Select(item => item.Content), variant.Select(item => item.Content));
    }

    [Fact]
    public void Chunker_skips_synthetic_iaSummary_content_and_keeps_siblings()
    {
        using var synthetic = JsonDocument.Parse("""
        {
          "request_status": "ok",
          "page": 1,
          "page_count": 1,
          "all_pages_count": 1,
          "all_count": 1,
          "page_data": [
            {
              "response_id": 1,
              "user_id": 1,
              "request_id": 1,
              "origin": "response",
              "origin_id": "syn-1",
              "response_type": "summary",
              "response_data": {
                "iaSummary": "token exclusivo iaSummary SYNTHETIC_POLICY_FIXTURE",
                "data": ["campo irmão preservado"],
                "origin": "response"
              },
              "cached": false,
              "request_created_at": "2026-09-06T00:00:00Z",
              "tags": [],
              "created_at": "2026-09-06T00:00:00Z",
              "updated_at": "2026-09-06T00:00:00Z"
            }
          ]
        }
        """);

        var chunk = Assert.Single(new LocalRagChunker().BuildChunks(synthetic, FixturePath));
        Assert.DoesNotContain("iaSummary", chunk.Content, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("campo irmão preservado", chunk.Content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Search_returns_empty_for_negative_queries()
    {
        using var fixture = JsonDocument.Parse(File.ReadAllText(FixturePath));
        var index = new LocalRagIndex(new LocalRagChunker().BuildChunks(fixture, FixturePath));

        var hits = index.Search("6003160-36.2026.8.16.0021", "zzzzzzzzzzzzzzzz", 5);

        Assert.Empty(hits);
    }

    private static string CreateReorderedFixtureJson()
    {
        using var fixture = JsonDocument.Parse(File.ReadAllText(FixturePath));
        var root = JsonNode.Parse(fixture.RootElement.GetRawText())!.AsObject();
        var pageData = root["page_data"]!.AsArray();
        var lawsuit = pageData[0]!.AsObject();
        var responseData = lawsuit["response_data"]!.AsObject();

        var reorderedResponse = new JsonObject
        {
            ["updated_at"] = responseData["updated_at"]!.DeepClone(),
            ["created_at"] = responseData["created_at"]!.DeepClone(),
            ["tags"] = responseData["tags"]!.DeepClone(),
            ["crawler"] = responseData["crawler"]!.DeepClone(),
            ["attachments"] = responseData["attachments"]!.DeepClone(),
            ["steps"] = responseData["steps"]!.DeepClone(),
            ["last_step"] = responseData["last_step"]!.DeepClone(),
            ["related_lawsuits"] = responseData["related_lawsuits"]!.DeepClone(),
            ["phase_history"] = responseData["phase_history"]!.DeepClone(),
            ["pipelines"] = responseData["pipelines"]!.DeepClone(),
            ["parties"] = responseData["parties"]!.DeepClone(),
            ["subjects"] = responseData["subjects"]!.DeepClone(),
            ["classifications"] = responseData["classifications"]!.DeepClone(),
            ["code"] = responseData["code"]!.DeepClone(),
            ["instance"] = responseData["instance"]!.DeepClone(),
            ["name"] = responseData["name"]!.DeepClone(),
            ["secrecy_level"] = responseData["secrecy_level"]!.DeepClone(),
            ["tribunal_acronym"] = responseData["tribunal_acronym"]!.DeepClone(),
            ["justice"] = responseData["justice"]!.DeepClone(),
            ["justice_description"] = responseData["justice_description"]!.DeepClone(),
            ["tribunal"] = responseData["tribunal"]!.DeepClone(),
            ["county"] = responseData["county"]!.DeepClone(),
            ["state"] = responseData["state"]!.DeepClone(),
            ["city"] = responseData["city"]!.DeepClone(),
            ["area"] = responseData["area"]!.DeepClone(),
            ["amount"] = responseData["amount"]!.DeepClone(),
            ["distribution_date"] = responseData["distribution_date"]!.DeepClone(),
            ["situation"] = responseData["situation"]!.DeepClone(),
            ["judge"] = responseData["judge"]!.DeepClone(),
            ["free_justice"] = responseData["free_justice"]!.DeepClone(),
            ["system"] = responseData["system"]!.DeepClone(),
            ["tribunal_url"] = responseData["tribunal_url"]!.DeepClone(),
            ["status"] = responseData["status"]!.DeepClone(),
            ["phase"] = responseData["phase"]!.DeepClone()
        };

        lawsuit["response_data"] = reorderedResponse;
        lawsuit["unknown_top_level_field"] = "ignored";
        root["page_data"] = pageData;
        return root.ToJsonString();
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
            duration_ms = 0,
            per_query_results = result.PerQueryResults
        };

        File.WriteAllText(outputPath, JsonSerializer.Serialize(snapshot, SnapshotSerializerOptions) + Environment.NewLine);
        Assert.True(File.Exists(outputPath));
    }

    [Fact]
    public void Write_mvp_local_validation_snapshot()
    {
        using var fixture = JsonDocument.Parse(File.ReadAllText(FixturePath));
        using var querySet = JsonDocument.Parse(File.ReadAllText(QuerySetPath));
        var evaluator = new LocalRagEvaluator(new LocalRagChunker());

        var result = evaluator.Evaluate(fixture, querySet, FixturePath, "baseline-lexical", 5);
        var outputPath = Path.Combine(RepoRoot, "docs", "onda4", "mvp-local-validation.v1.json");
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

        var snapshot = new
        {
            mvp_validation_version = "rj-mvp-local-validation-v1",
            git_commit = "fa1f36bae9626f25356b7c38a8f193f0961315c7",
            dataset_version = "rj-juridical-response-60031603620268160021-2026-09-06",
            query_set_version = "rj-juridical-response-query-set-v2",
            fixture_hash = "b5decf20e6bb330a7a58974711b7ec8e72215d74568e014e8cf1ced14ee916aa",
            baseline_metrics = new { hit_at_1 = 20, hit_at_3 = 24, hit_at_5 = 24, mrr = 0.9166666666666666 },
            final_metrics = new { hit_at_1 = result.HitAt1, hit_at_3 = result.HitAt3, hit_at_5 = result.HitAt5, mrr = result.Mrr },
            robustness_cases = new[]
            {
                "synthetic iaSummary exclusion",
                "unknown fields",
                "property reordering",
                "negative queries",
                "exact identifier retrieval"
            },
            negative_queries = new[] { "zzzzzzzzzzzzzzzz" },
            determinism_result = new { chunk_ids_stable = true, retrieval_repeatable = true },
            gate_results = new[] { "LocalRagEvaluationTests", "ResponseFixtureIngestionTests", "Wave1CorpusContractTests" },
            blockers = new[] { "BLK-POSTGRES-001" },
            known_failures = new[] { "OabRulingBrAbRunnerTests" },
            threats_to_validity = new[]
            {
                "only one real legal fixture",
                "only one process",
                "only one tribunal",
                "iaSummary literal not observed in real corpus",
                "attachments without binary content",
                "generalization between tribunals not demonstrated",
                "PostgreSQL validation blocked"
            }
        };

        File.WriteAllText(outputPath, JsonSerializer.Serialize(snapshot, SnapshotSerializerOptions) + Environment.NewLine);
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
