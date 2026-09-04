using RJ.Application.Evaluation;

namespace RJ.Application.Benchmarking;

public sealed record GenerationBenchmarkCatalog(
    string Version,
    IReadOnlyList<GenerationEvaluationCase> Cases)
{
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Version))
        {
            throw new ArgumentException("Benchmark catalog version cannot be empty.", nameof(Version));
        }

        ArgumentNullException.ThrowIfNull(Cases);
        if (Cases.Count == 0)
        {
            throw new InvalidOperationException("Benchmark catalog must contain at least one evaluation case.");
        }

        var duplicateId = Cases
            .GroupBy(item => item.Id, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1)?.Key;

        if (duplicateId is not null)
        {
            throw new InvalidOperationException($"Benchmark catalog contains duplicate case id '{duplicateId}'.");
        }

        if (Cases.Any(item => string.IsNullOrWhiteSpace(item.Id)))
        {
            throw new InvalidOperationException("Benchmark case ids cannot be empty.");
        }
    }
}
