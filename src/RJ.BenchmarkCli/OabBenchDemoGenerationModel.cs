using RJ.Application.Generation;

namespace RJ.BenchmarkCli;

public sealed class OabBenchDemoGenerationModel : IGenerationModel
{
    public const string ModelId = "oab-bench-demo-v2";
    private static readonly char[] LineSeparators = ['\r', '\n'];

    public Task<GenerationModelOutput> GenerateAsync(
        GenerationContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        if (context.Items.Count == 0)
        {
            throw new InvalidOperationException("OAB-Bench demo target requires at least one context item.");
        }

        var item = context.Items[0];
        var claimTexts = BuildClaimTexts(context.Query, item.Excerpt);
        var claims = claimTexts.Select(text => new GenerationClaim(
            text,
            [
                new GenerationCitation(
                    item.DocumentId,
                    item.ContentSha256,
                    item.Position.StartOffset,
                    item.Position.Length)
            ])).ToArray();

        return Task.FromResult(new GenerationModelOutput(
            false,
            null,
            claims));
    }

    private static string[] BuildClaimTexts(string query, string excerpt)
    {
        var turns = ExtractTurns(query);
        if (turns.Length == 0)
        {
            return [excerpt];
        }

        return turns.Select(BuildClaimTextFromTurn).ToArray();
    }

    private static string[] ExtractTurns(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return [];
        }

        var lines = query.Split(LineSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var turnLines = lines
            .Where(line => char.IsDigit(line.FirstOrDefault()) && line.Contains('.'))
            .Select(line => line[(line.IndexOf('.') + 1)..].Trim())
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .ToArray();

        return turnLines.Length > 0
            ? turnLines
            : [lines.FirstOrDefault() ?? string.Empty];
    }

    private static string BuildClaimTextFromTurn(string turn)
    {
        var normalized = turn.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return "Sim.";
        }

        if (normalized.StartsWith("Qual ", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("Quais ", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("Onde ", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("Quando ", StringComparison.OrdinalIgnoreCase))
        {
            return normalized;
        }

        if (normalized.StartsWith("A ", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("O ", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("As ", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("Os ", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("Maria ", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("Fernanda ", StringComparison.OrdinalIgnoreCase))
        {
            return $"Sim, {char.ToLowerInvariant(normalized[0])}{normalized[1..]}";
        }

        return normalized;
    }
}
