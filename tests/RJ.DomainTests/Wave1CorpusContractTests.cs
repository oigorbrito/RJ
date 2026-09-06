using System.Security.Cryptography;
using System.Text.Json;

namespace RJ.DomainTests;

public sealed class Wave1CorpusContractTests
{
    private static readonly string RepoRoot = FindRepoRoot();
    private static readonly string FixturePath = Path.Combine(RepoRoot, "tests", "fixtures", "rj", "response_60031603620268160021_1.json");

    [Fact]
    public void Real_fixture_exists_and_parses()
    {
        Assert.True(File.Exists(FixturePath));
        Assert.Equal("b5decf20e6bb330a7a58974711b7ec8e72215d74568e014e8cf1ced14ee916aa", ComputeSha256(FixturePath));

        using var doc = JsonDocument.Parse(File.ReadAllText(FixturePath));
        Assert.Equal(2, doc.RootElement.GetProperty("page_data").GetArrayLength());
        Assert.Contains(doc.RootElement.GetProperty("page_data").EnumerateArray(), item => item.GetProperty("response_type").GetString() == "summary");
    }

    [Fact]
    public void Contract_dataset_and_query_set_are_versioned_and_loadable()
    {
        using var contract = JsonDocument.Parse(File.ReadAllText(Path.Combine(RepoRoot, "docs", "onda1", "rj-corpus-contract.v2.json")));
        using var dataset = JsonDocument.Parse(File.ReadAllText(Path.Combine(RepoRoot, "docs", "onda1", "rj-dataset-baseline.v2.json")));
        using var querySet = JsonDocument.Parse(File.ReadAllText(Path.Combine(RepoRoot, "docs", "onda1", "rj-query-set.v2.json")));

        Assert.Equal("rj-corpus-contract-v2", contract.RootElement.GetProperty("schema_version").GetString());
        Assert.Equal("rj-juridical-response-60031603620268160021-2026-09-06", contract.RootElement.GetProperty("dataset_version").GetString());
        Assert.Equal("rj-juridical-response-60031603620268160021-2026-09-06", dataset.RootElement.GetProperty("dataset_version").GetString());
        Assert.Equal("rj-juridical-response-query-set-v2", querySet.RootElement.GetProperty("query_set_version").GetString());
        Assert.Contains(contract.RootElement.GetProperty("fields").EnumerateArray(), field => field.GetProperty("path").GetString() == "page_data[].response_data.parties[].lawyers[].documents[]");
        Assert.Contains(querySet.RootElement.GetProperty("queries").EnumerateArray(), query => query.GetProperty("query_id").GetString() == "q-024");
    }

    [Fact]
    public void Policy_marks_iaSummary_excluded_and_does_not_remove_summary()
    {
        using var contract = JsonDocument.Parse(File.ReadAllText(Path.Combine(RepoRoot, "docs", "onda1", "rj-corpus-contract.v2.json")));
        using var fixture = JsonDocument.Parse(File.ReadAllText(FixturePath));

        var excluded = contract.RootElement.GetProperty("rag_policy").GetProperty("excluded").EnumerateArray().Select(item => item.GetString()).ToArray();
        Assert.Contains("iaSummary", excluded);
        Assert.Contains("page_data[].response_type=summary", excluded);
        Assert.Contains(fixture.RootElement.GetProperty("page_data").EnumerateArray(), item => item.GetProperty("response_type").GetString() == "summary");
    }

    [Fact]
    public void Query_set_oracles_resolve_against_fixture()
    {
        using var fixture = JsonDocument.Parse(File.ReadAllText(FixturePath));
        using var querySet = JsonDocument.Parse(File.ReadAllText(Path.Combine(RepoRoot, "docs", "onda1", "rj-query-set.v2.json")));

        foreach (var query in querySet.RootElement.GetProperty("queries").EnumerateArray())
        {
            var oraclePath = query.GetProperty("oracle_path").GetString()!;
            Assert.True(ResolveOracle(fixture.RootElement, oraclePath), oraclePath);
        }
    }

    private static bool ResolveOracle(JsonElement root, string oraclePath)
    {
        return oraclePath switch
        {
            "page_data[0].response_data.code" => Get(root, "page_data", 0, "response_data", "code"),
            "page_data[0].response_data.tribunal_acronym" => Get(root, "page_data", 0, "response_data", "tribunal_acronym"),
            "page_data[0].response_data.county" => Get(root, "page_data", 0, "response_data", "county"),
            "page_data[0].response_data.city" => Get(root, "page_data", 0, "response_data", "city"),
            "page_data[0].response_data.state" => Get(root, "page_data", 0, "response_data", "state"),
            "page_data[0].response_data.amount" => Get(root, "page_data", 0, "response_data", "amount"),
            "page_data[0].response_data.judge" => Get(root, "page_data", 0, "response_data", "judge"),
            "page_data[0].response_data.status" => Get(root, "page_data", 0, "response_data", "status"),
            "page_data[0].response_data.phase" => Get(root, "page_data", 0, "response_data", "phase"),
            "page_data[0].response_data.parties[].name" => Get(root, "page_data", 0, "response_data", "parties", 0, "name"),
            "page_data[0].response_data.parties[3].name" => Get(root, "page_data", 0, "response_data", "parties", 3, "name"),
            "page_data[0].response_data.parties[0].lawyers[0].name" => Get(root, "page_data", 0, "response_data", "parties", 0, "lawyers", 0, "name"),
            "page_data[0].response_data.parties[0].lawyers[0].documents[0].document" => Get(root, "page_data", 0, "response_data", "parties", 0, "lawyers", 0, "documents", 0, "document"),
            "page_data[0].response_data.parties[0].main_document" => Get(root, "page_data", 0, "response_data", "parties", 0, "main_document"),
            "page_data[0].response_data.subjects[].name" => Get(root, "page_data", 0, "response_data", "subjects", 0, "name"),
            "page_data[0].response_data.classifications[].name" => Get(root, "page_data", 0, "response_data", "classifications", 0, "name"),
            "page_data[0].response_data.last_step.content" => Get(root, "page_data", 0, "response_data", "last_step", "content"),
            "page_data[0].response_data.last_step.step_date" => Get(root, "page_data", 0, "response_data", "last_step", "step_date"),
            "page_data[0].response_data.last_step.steps_count" => Get(root, "page_data", 0, "response_data", "last_step", "steps_count"),
            "page_data[0].response_data.steps[3].content" => Get(root, "page_data", 0, "response_data", "steps", 3, "content"),
            "page_data[0].response_data.attachments.length" => root.GetProperty("page_data").EnumerateArray().First().GetProperty("response_data").GetProperty("attachments").GetArrayLength() == 2,
            "page_data[0].response_data.attachments[0].attachment_id" => Get(root, "page_data", 0, "response_data", "attachments", 0, "attachment_id"),
            "page_data[0].response_data.attachments[0].extension" => Get(root, "page_data", 0, "response_data", "attachments", 0, "extension"),
            "page_data[0].response_data.crawler.source_name" => Get(root, "page_data", 0, "response_data", "crawler", "source_name"),
            _ => false
        };
    }

    private static bool Get(JsonElement root, params object[] path)
    {
        var current = root;
        foreach (var segment in path)
        {
            if (segment is int index)
            {
                current = current.EnumerateArray().ElementAt(index);
            }
            else
            {
                current = current.GetProperty((string)segment);
            }
        }

        return true;
    }

    private static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
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
