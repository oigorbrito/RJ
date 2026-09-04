using RJ.Application.Evaluation;
using RJ.Application.Generation;

namespace RJ.Application.Benchmarking;

public sealed class GenerationBenchmarkRunner(
    IGenerationModel model,
    GenerationEvaluator evaluator)
{
    private readonly IGenerationModel _model = model ?? throw new ArgumentNullException(nameof(model));
    private readonly GenerationEvaluator _evaluator = evaluator ?? throw new ArgumentNullException(nameof(evaluator));

    public async Task<GenerationBenchmarkReport> RunAsync(
        GenerationBenchmarkCatalog catalog,
        GenerationBenchmarkMetadata metadata,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(metadata);

        catalog.Validate();
        metadata.Validate();

        if (!StringComparer.Ordinal.Equals(catalog.Version, metadata.CatalogVersion))
        {
            throw new InvalidOperationException("Benchmark metadata catalog version must match the supplied catalog version.");
        }

        var caseReports = new List<GenerationBenchmarkCaseReport>(catalog.Cases.Count);

        foreach (var evaluationCase in catalog.Cases.OrderBy(item => item.Id, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var output = await _model.GenerateAsync(evaluationCase.Context, cancellationToken)
                    ?? throw new InvalidOperationException("Generation model returned no structured output.");

                var evaluation = _evaluator.Evaluate(evaluationCase, output);
                caseReports.Add(new GenerationBenchmarkCaseReport(
                    evaluationCase.Id,
                    evaluation.Passed,
                    evaluation,
                    null,
                    null));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                caseReports.Add(new GenerationBenchmarkCaseReport(
                    evaluationCase.Id,
                    false,
                    null,
                    exception.GetType().FullName,
                    exception.Message));
            }
        }

        var evaluated = caseReports
            .Where(item => item.Evaluation is not null)
            .Select(item => item.Evaluation!)
            .ToArray();

        var minimumClaimRecall = evaluated.Length == 0 ? 0.0 : evaluated.Min(item => item.ClaimRecall);
        var minimumCitationValidity = evaluated.Length == 0 ? 0.0 : evaluated.Min(item => item.CitationValidity);
        var minimumGroundedness = evaluated.Length == 0 ? 0.0 : evaluated.Min(item => item.Groundedness);
        var passedCases = caseReports.Count(item => item.Passed);
        var failedCases = caseReports.Count - passedCases;

        var passed = failedCases == 0
            && minimumClaimRecall == 1.0
            && minimumCitationValidity == 1.0
            && minimumGroundedness == 1.0;

        return new GenerationBenchmarkReport(
            metadata,
            caseReports.Count,
            passedCases,
            failedCases,
            minimumClaimRecall,
            minimumCitationValidity,
            minimumGroundedness,
            passed,
            caseReports);
    }
}
