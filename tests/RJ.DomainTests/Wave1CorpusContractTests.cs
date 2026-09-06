using System.Text.Json;

namespace RJ.DomainTests;

public sealed class Wave1CorpusContractTests
{
    [Fact]
    public void Contract_and_query_set_are_versioned_and_loadable()
    {
        using var contract = JsonDocument.Parse(File.ReadAllText(RepoPath("docs", "onda1", "rj-corpus-contract.v1.json")));
        using var dataset = JsonDocument.Parse(File.ReadAllText(RepoPath("docs", "onda1", "rj-dataset-baseline.v1.json")));
        using var querySet = JsonDocument.Parse(File.ReadAllText(RepoPath("docs", "onda1", "rj-query-set.v1.json")));

        Assert.Equal("rj-corpus-contract-v1", contract.RootElement.GetProperty("schema_version").GetString());
        Assert.Equal("rj-oab-bench-and-rulingbr-2026-09-06", contract.RootElement.GetProperty("dataset_version").GetString());
        Assert.Equal("rj-oab-bench-and-rulingbr-2026-09-06", dataset.RootElement.GetProperty("dataset_version").GetString());
        Assert.Equal("rj-oab-bench-query-set-v1", querySet.RootElement.GetProperty("query_set_version").GetString());
        Assert.True(contract.RootElement.GetProperty("fields").GetArrayLength() >= 10);
        Assert.True(querySet.RootElement.GetProperty("queries").GetArrayLength() >= 3);
    }

    [Fact]
    public void Contract_marks_iaSummary_as_unverified_and_excluded_from_RAG()
    {
        using var contract = JsonDocument.Parse(File.ReadAllText(RepoPath("docs", "onda1", "rj-corpus-contract.v1.json")));
        var fields = contract.RootElement.GetProperty("fields");
        var iaSummary = fields.EnumerateArray().Single(field => field.GetProperty("path").GetString() == "iaSummary");

        Assert.Equal("UNVERIFIED", iaSummary.GetProperty("status").GetString());
        Assert.Equal("UNDECIDED", iaSummary.GetProperty("classification").GetString());

        var ragPolicy = contract.RootElement.GetProperty("rag_policy");
        var excluded = ragPolicy.GetProperty("excluded").EnumerateArray().Select(item => item.GetString()).ToArray();
        Assert.Contains("iaSummary", excluded);
    }

    [Fact]
    public void Question_and_answer_corpus_files_remain_present()
    {
        Assert.True(File.Exists(RepoPath("oab-bench", "data", "oab_bench", "question.jsonl")));
        Assert.True(File.Exists(RepoPath("oab-bench", "data", "oab_bench", "reference_answer", "guidelines.jsonl")));
        Assert.True(Directory.Exists(RepoPath("oab-bench", "data", "oab_bench", "model_answer")));
        Assert.True(Directory.Exists(RepoPath("oab-bench", "data", "oab_bench", "model_judgment")));
        Assert.True(File.Exists(RepoPath("rulingbr", "sample-5.json")));
    }

    private static string RepoPath(params string[] parts)
    {
        var current = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(current, "RJ.slnx")))
        {
            var parent = Directory.GetParent(current);
            if (parent is null)
            {
                throw new InvalidOperationException("Repository root not found.");
            }

            current = parent.FullName;
        }

        return parts.Aggregate(current, Path.Combine);
    }
}
