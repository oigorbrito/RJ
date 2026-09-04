using RJ.Application.Retrieval;

namespace RJ.Application.Generation;

public sealed class GenerationContextBuilder
{
    public GenerationContext Build(
        string caseId,
        string query,
        int characterBudget,
        IReadOnlyCollection<LegalEvidenceHit> evidence)
    {
        if (string.IsNullOrWhiteSpace(caseId))
        {
            throw new ArgumentException("Case identifier cannot be empty.", nameof(caseId));
        }

        if (string.IsNullOrWhiteSpace(query))
        {
            throw new ArgumentException("Generation query cannot be empty.", nameof(query));
        }

        if (characterBudget <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(characterBudget), characterBudget, "Character budget must be positive.");
        }

        ArgumentNullException.ThrowIfNull(evidence);

        var normalizedCaseId = caseId.Trim();
        var normalizedQuery = query.Trim();

        var ordered = evidence
            .Where(item => StringComparer.Ordinal.Equals(item.CaseId, normalizedCaseId))
            .Where(item => !string.IsNullOrEmpty(item.Excerpt))
            .OrderByDescending(item => item.Rank)
            .ThenBy(item => item.DocumentId, StringComparer.Ordinal)
            .ThenBy(item => item.Position.StartOffset)
            .ThenBy(item => item.Position.Length)
            .ToArray();

        if (ordered.Length != evidence.Count)
        {
            throw new InvalidOperationException("Generation context evidence must be non-empty and belong exclusively to the requested case.");
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var items = new List<GenerationContextItem>();
        var usedCharacters = 0;

        foreach (var item in ordered)
        {
            var key = $"{item.DocumentId}\u001f{item.ContentSha256}\u001f{item.Position.StartOffset}\u001f{item.Position.Length}";

            if (!seen.Add(key))
            {
                continue;
            }

            if (item.Excerpt.Length != item.Position.Length)
            {
                throw new InvalidOperationException("Evidence excerpt length must match its source position length.");
            }

            if (item.Excerpt.Length > characterBudget - usedCharacters)
            {
                continue;
            }

            items.Add(new GenerationContextItem(
                item.CaseId,
                item.DocumentId,
                item.SourceName,
                item.ContentSha256,
                item.Excerpt,
                item.Position,
                item.Rank));
            usedCharacters += item.Excerpt.Length;
        }

        return new GenerationContext(
            normalizedCaseId,
            normalizedQuery,
            characterBudget,
            usedCharacters,
            items);
    }
}
