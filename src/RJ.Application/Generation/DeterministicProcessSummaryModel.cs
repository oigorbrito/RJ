namespace RJ.Application.Generation;

public sealed class DeterministicProcessSummaryModel : IGenerationModel
{
    public Task<GenerationModelOutput> GenerateAsync(
        GenerationContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        if (!context.Query.Contains(ProcessSummaryPrompt.PromptId, StringComparison.Ordinal)
            || !context.Query.Contains(ProcessSummaryPrompt.PromptVersion, StringComparison.Ordinal))
        {
            return Task.FromResult(new GenerationModelOutput(
                true,
                "Process summary prompt identity is missing.",
                Array.Empty<GenerationClaim>()));
        }

        var claims = context.Items
            .OrderBy(item => item.Position.StartOffset)
            .ThenBy(item => item.DocumentId, StringComparer.Ordinal)
            .Where(IsClaimWorthy)
            .Select(item => new GenerationClaim(
                ToClaimText(item.Excerpt),
                [new GenerationCitation(item.DocumentId, item.ContentSha256, item.Position.StartOffset, item.Position.Length)]))
            .ToArray();

        if (claims.Length == 0)
        {
            return Task.FromResult(new GenerationModelOutput(
                true,
                "No process evidence was available for deterministic summary.",
                Array.Empty<GenerationClaim>()));
        }

        return Task.FromResult(new GenerationModelOutput(false, null, claims));
    }

    private static bool IsClaimWorthy(GenerationContextItem item) =>
        item.Excerpt.StartsWith("CNJ:", StringComparison.Ordinal)
        || item.Excerpt.StartsWith("Nome:", StringComparison.Ordinal)
        || item.Excerpt.StartsWith("Juizo:", StringComparison.Ordinal)
        || item.Excerpt.StartsWith("Fase:", StringComparison.Ordinal)
        || item.Excerpt.StartsWith("Status:", StringComparison.Ordinal)
        || item.Excerpt.StartsWith("Sigilo:", StringComparison.Ordinal)
        || item.Excerpt.StartsWith("Valor da causa:", StringComparison.Ordinal)
        || item.Excerpt.StartsWith("Parte:", StringComparison.Ordinal)
        || item.Excerpt.StartsWith("Advogado:", StringComparison.Ordinal)
        || item.Excerpt.StartsWith("Classe:", StringComparison.Ordinal)
        || item.Excerpt.StartsWith("Assunto:", StringComparison.Ordinal)
        || item.Excerpt.StartsWith("Movimentacao:", StringComparison.Ordinal)
        || item.Excerpt.StartsWith("Inconsistencia:", StringComparison.Ordinal)
        || item.Excerpt.StartsWith("Anexo metadata:", StringComparison.Ordinal);

    private static string ToClaimText(string excerpt) =>
        excerpt.StartsWith("Anexo metadata:", StringComparison.Ordinal)
            ? $"{excerpt}; conteudo de anexo nao utilizado para conclusoes."
            : excerpt;
}
