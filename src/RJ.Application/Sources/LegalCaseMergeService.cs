using RJ.Domain.Cases;

namespace RJ.Application.Sources;

public static class LegalCaseMergeService
{
    public static LegalCase Merge(IReadOnlyList<LegalCase> cases, IReadOnlyList<string> sourcePriority)
    {
        ArgumentNullException.ThrowIfNull(cases);
        ArgumentNullException.ThrowIfNull(sourcePriority);

        if (cases.Count == 0)
        {
            throw new ArgumentException("At least one legal case is required.", nameof(cases));
        }

        var normalizedPriority = sourcePriority
            .Select((sourceName, index) => new { SourceName = Require(sourceName, nameof(sourcePriority)), Index = index })
            .ToDictionary(item => item.SourceName, item => item.Index, StringComparer.OrdinalIgnoreCase);

        var cnj = cases[0].Cnj.Value;
        foreach (var legalCase in cases)
        {
            if (!StringComparer.Ordinal.Equals(cnj, legalCase.Cnj.Value))
            {
                throw new InvalidOperationException("Cannot merge legal cases with different CNJ values.");
            }
        }

        var primary = cases
            .OrderBy(legalCase => SourceRank(legalCase, normalizedPriority))
            .ThenBy(legalCase => legalCase.Id.Value, StringComparer.Ordinal)
            .First();

        var provenance = cases
            .SelectMany(legalCase => legalCase.Provenance)
            .OrderBy(item => item.FieldPath, StringComparer.Ordinal)
            .ThenBy(item => SourceRank(item.SourceName, normalizedPriority))
            .ThenBy(item => item.SourceName, StringComparer.Ordinal)
            .ThenBy(item => item.ObservedPath, StringComparer.Ordinal)
            .ToArray();

        return new LegalCase(
            primary.Id,
            primary.Cnj,
            primary.Name,
            primary.Court,
            primary.Phase,
            primary.Status,
            primary.Amount,
            primary.Parties,
            primary.Lawyers,
            primary.Classifications,
            primary.Subjects,
            primary.Steps,
            primary.Attachments,
            provenance);
    }

    private static int SourceRank(LegalCase legalCase, IReadOnlyDictionary<string, int> sourcePriority) =>
        legalCase.Provenance
            .Select(item => SourceRank(item.SourceName, sourcePriority))
            .DefaultIfEmpty(int.MaxValue)
            .Min();

    private static int SourceRank(string sourceName, IReadOnlyDictionary<string, int> sourcePriority) =>
        sourcePriority.TryGetValue(sourceName, out var index) ? index : int.MaxValue;

    private static string Require(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Value cannot be empty.", parameterName);
        }

        return value.Trim();
    }
}
