using RJ.Application.Generation;

namespace RJ.BenchmarkCli;

public sealed class HarnessSelfTestGenerationModel : IGenerationModel
{
    public const string ModelId = "harness-selftest-v1";

    public Task<GenerationModelOutput> GenerateAsync(
        GenerationContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        if (StringComparer.Ordinal.Equals(context.CaseId, "case-abstain-1"))
        {
            return Task.FromResult(new GenerationModelOutput(
                true,
                "Fixture requires abstention.",
                []));
        }

        if (context.Items.Count == 0)
        {
            throw new InvalidOperationException("Self-test model requires at least one context item.");
        }

        var item = context.Items[0];

        return Task.FromResult(new GenerationModelOutput(
            false,
            null,
            [new GenerationClaim(
                context.Query,
                [new GenerationCitation(
                    item.DocumentId,
                    item.ContentSha256,
                    item.Position.StartOffset,
                    item.Position.Length)])]));
    }
}
