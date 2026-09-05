using RJ.Application.Generation;

namespace RJ.BenchmarkCli;

public sealed class OabBenchDemoGenerationModel : IGenerationModel
{
    public const string ModelId = "oab-bench-demo-v1";
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
        var claimText = BuildClaimText(context.Query, item.Excerpt);

        return Task.FromResult(new GenerationModelOutput(
            false,
            null,
            [
                new GenerationClaim(
                    claimText,
                    [
                        new GenerationCitation(
                            item.DocumentId,
                            item.ContentSha256,
                            item.Position.StartOffset,
                            item.Position.Length)
                    ])
            ]));
    }

    private static string BuildClaimText(string query, string excerpt)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return excerpt;
        }

        var firstLine = query.Split(LineSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault();

        return string.IsNullOrWhiteSpace(firstLine)
            ? excerpt
            : firstLine;
    }
}
