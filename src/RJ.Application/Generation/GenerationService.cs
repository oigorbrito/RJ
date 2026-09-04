namespace RJ.Application.Generation;

public sealed class GenerationService(IGenerationModel model)
{
    private readonly IGenerationModel _model = model ?? throw new ArgumentNullException(nameof(model));

    public async Task<GenerationModelOutput> GenerateAsync(
        GenerationContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Items.Count == 0)
        {
            throw new InvalidOperationException("Generation cannot run without cited context evidence.");
        }

        var output = await _model.GenerateAsync(context, cancellationToken)
            ?? throw new InvalidOperationException("Generation model returned no structured output.");

        ArgumentNullException.ThrowIfNull(output.Claims);

        foreach (var claim in output.Claims)
        {
            ArgumentNullException.ThrowIfNull(claim);

            if (string.IsNullOrWhiteSpace(claim.Text))
            {
                throw new InvalidOperationException("Generated claims cannot be empty.");
            }

            ArgumentNullException.ThrowIfNull(claim.Citations);
            if (claim.Citations.Count == 0)
            {
                throw new InvalidOperationException("Every generated claim must include at least one citation.");
            }

            foreach (var citation in claim.Citations)
            {
                ArgumentNullException.ThrowIfNull(citation);

                var matchesContext = context.Items.Any(item =>
                    StringComparer.Ordinal.Equals(item.DocumentId, citation.DocumentId)
                    && StringComparer.Ordinal.Equals(item.ContentSha256, citation.ContentSha256)
                    && item.Position.StartOffset == citation.StartOffset
                    && item.Position.Length == citation.Length);

                if (!matchesContext)
                {
                    throw new InvalidOperationException("Generated claim references evidence outside the supplied generation context.");
                }
            }
        }

        return output;
    }
}
