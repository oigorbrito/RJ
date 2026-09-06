using System.Text.Json;

namespace RJ.Application.Rag;

public sealed class LocalRagEvaluator(LocalRagChunker chunker)
{
    public LocalRagEvaluationResult Evaluate(
        JsonDocument fixture,
        JsonDocument querySet,
        string fixturePath,
        string strategyName,
        int limit)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        ArgumentNullException.ThrowIfNull(querySet);
        ArgumentException.ThrowIfNullOrWhiteSpace(fixturePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(strategyName);

        var chunks = chunker.BuildChunks(fixture, fixturePath);
        var index = new LocalRagIndex(chunks);
        var queries = querySet.RootElement.GetProperty("queries").EnumerateArray().ToArray();
        var results = new List<LocalRagPerQueryResult>(queries.Length);
        var hitsAt1 = 0;
        var hitsAt3 = 0;
        var hitsAt5 = 0;
        var reciprocalRank = 0d;
        var start = DateTimeOffset.UtcNow;

        foreach (var query in queries)
        {
            var queryId = query.GetProperty("query_id").GetString() ?? string.Empty;
            var question = query.GetProperty("question").GetString() ?? string.Empty;
            var oraclePath = query.GetProperty("oracle_path").GetString() ?? string.Empty;
            var expectedAnswer = query.GetProperty("expected_answer").GetString() ?? string.Empty;
            var retrieved = index.Search("6003160-36.2026.8.16.0021", question, limit).ToArray();
            var ranked = retrieved
                .Select((chunk, idx) => (chunk, rank: idx + 1))
                .FirstOrDefault(item => MatchesOracle(item.chunk, oraclePath, expectedAnswer));

            var status = ranked.chunk is null ? "MISS" : "HIT";
            if (ranked.chunk is not null)
            {
                reciprocalRank += 1d / ranked.rank;
                if (ranked.rank <= 1) hitsAt1++;
                if (ranked.rank <= 3) hitsAt3++;
                if (ranked.rank <= 5) hitsAt5++;
            }

            results.Add(new LocalRagPerQueryResult(
                queryId,
                question,
                expectedAnswer,
                oraclePath,
                ranked.chunk?.ChunkId,
                ranked.chunk is null ? null : $"{ranked.chunk.ResponseType}:{ranked.chunk.Section}",
                ranked.chunk is null ? null : ranked.rank,
                status));
        }

        var duration = DateTimeOffset.UtcNow - start;
        return new LocalRagEvaluationResult(
            strategyName,
            queries.Length,
            hitsAt1,
            hitsAt3,
            hitsAt5,
            reciprocalRank / queries.Length,
            duration,
            results);
    }

    private static bool MatchesOracle(LocalRagChunk chunk, string oraclePath, string expectedAnswer)
    {
        if (string.IsNullOrWhiteSpace(expectedAnswer))
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(chunk.Content))
        {
            return false;
        }

        var parts = expectedAnswer
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .SelectMany(part => part.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Where(part => part.Length > 0)
            .ToArray();

        if (parts.Length == 0)
        {
            parts = [expectedAnswer.Trim()];
        }

        return parts.All(part =>
            chunk.Content.Contains(part, StringComparison.OrdinalIgnoreCase)
            || chunk.Metadata.Values.Any(value => value.Contains(part, StringComparison.OrdinalIgnoreCase)));
    }
}
