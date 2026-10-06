using System.Text.Json;

namespace RJ.DomainTests;

public sealed class RjudiProcessBenchmarkProtocolTests
{
    [Fact]
    public void Protocol_freezes_question_unit_measurements_decision_rule_and_validity_scope()
    {
        var path = Path.Combine(RepositoryRoot(), "docs", "onda1", "rjudi-process-benchmark-protocol.v1.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;

        Assert.Equal("rjudi-process-benchmark-protocol-v1", root.GetProperty("protocol_version").GetString());
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("research_question").GetString()));
        Assert.Equal(
            "one admitted legal process case evaluated against its independently versioned structured oracle",
            root.GetProperty("experimental_unit").GetString());
        Assert.Contains(
            root.GetProperty("pre_specified_measurements").EnumerateArray().Select(item => item.GetString()),
            item => item == "non_compensable_gate_results_by_case");
        Assert.Contains(
            root.GetProperty("measurement_rules").EnumerateArray().Select(item => item.GetString()),
            item => item == "no_post_hoc_metric_can_become_an_acceptance_rule_for_this_protocol_version");
        Assert.Contains(
            root.GetProperty("decision_rules").EnumerateArray().Select(item => item.GetString()),
            item => item is not null && item.Contains("NO_CLEAR_WINNER", StringComparison.Ordinal));
        Assert.True(root.GetProperty("validity_threats").GetArrayLength() > 0);
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "RJ.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new DirectoryNotFoundException("Repository root containing RJ.slnx was not found.");
    }
}
