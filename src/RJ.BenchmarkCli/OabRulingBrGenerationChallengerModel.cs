using System.Text;
using RJ.Application.Generation;

namespace RJ.BenchmarkCli;

public sealed class OabRulingBrGenerationChallengerModel : IGenerationModel
{
    public const string ModelId = "oab-rulingbr-generation-challenger-v1";
    private readonly string _modelConfiguration;

    public OabRulingBrGenerationChallengerModel(string modelConfiguration)
    {
        if (string.IsNullOrWhiteSpace(modelConfiguration))
        {
            throw new ArgumentException("Model configuration is required for the RulingBR generation challenger.", nameof(modelConfiguration));
        }

        _modelConfiguration = modelConfiguration.Trim();
    }

    public Task<GenerationModelOutput> GenerateAsync(
        GenerationContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        if (context.Items.Count == 0)
        {
            throw new InvalidOperationException("RulingBR challenger requires cited context evidence.");
        }

        var item = SelectBestEvidence(context);
        var claims = new[]
        {
            new GenerationClaim(
                BuildClaimText(context.Query, item.Excerpt),
                [
                    new GenerationCitation(
                        item.DocumentId,
                        item.ContentSha256,
                        item.Position.StartOffset,
                        item.Position.Length)
                ])
        };

        return Task.FromResult(new GenerationModelOutput(
            false,
            null,
            claims));
    }

    private string BuildClaimText(string query, string excerpt)
    {
        var normalizedQuery = Normalize(query);
        var normalizedExcerpt = Normalize(excerpt);
        var bestFragment = SelectBestFragment(normalizedExcerpt, normalizedQuery);

        if (string.IsNullOrWhiteSpace(bestFragment))
        {
            return _modelConfiguration.StartsWith("abstain", StringComparison.OrdinalIgnoreCase)
                ? "Não há evidência suficiente para afirmar a resposta."
                : excerpt.Trim();
        }

        return bestFragment;
    }

    private static GenerationContextItem SelectBestEvidence(GenerationContext context)
    {
        var best = context.Items[0];
        var bestScore = ScoreEvidence(context.Query, best.Excerpt);

        foreach (var item in context.Items.Skip(1))
        {
            var score = ScoreEvidence(context.Query, item.Excerpt);
            if (score > bestScore
                || (score == bestScore && StringComparer.Ordinal.Compare(item.DocumentId, best.DocumentId) < 0)
                || (score == bestScore && StringComparer.Ordinal.Equals(item.DocumentId, best.DocumentId) && item.Position.StartOffset < best.Position.StartOffset)
                || (score == bestScore && StringComparer.Ordinal.Equals(item.DocumentId, best.DocumentId) && item.Position.StartOffset == best.Position.StartOffset && item.Position.Length < best.Position.Length))
            {
                best = item;
                bestScore = score;
            }
        }

        return best;
    }

    private static int ScoreEvidence(string query, string excerpt)
    {
        var normalizedQuery = Normalize(query);
        var normalizedExcerpt = Normalize(excerpt);
        var score = 0;

        foreach (var token in normalizedQuery.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (token.Length < 3)
            {
                continue;
            }

            if (normalizedExcerpt.Contains(token, StringComparison.Ordinal))
            {
                score++;
            }
        }

        return score;
    }

    private static string SelectBestFragment(string excerpt, string query)
    {
        var sentences = excerpt
            .Split(['.', ';', ':', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var best = string.Empty;
        var bestScore = -1;
        foreach (var sentence in sentences)
        {
            var score = 0;
            foreach (var token in query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (token.Length < 3)
                {
                    continue;
                }

                if (sentence.Contains(token, StringComparison.Ordinal))
                {
                    score++;
                }
            }

            if (score > bestScore)
            {
                best = sentence.Trim();
                bestScore = score;
            }
        }

        return best;
    }

    private static string Normalize(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (var character in text.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(character) || char.IsWhiteSpace(character))
            {
                builder.Append(character);
                continue;
            }

            builder.Append(' ');
        }

        return builder.ToString();
    }
}
