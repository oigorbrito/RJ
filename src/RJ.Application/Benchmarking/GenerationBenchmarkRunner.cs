using RJ.Application.Evaluation;
using RJ.Application.Generation;

namespace RJ.Application.Benchmarking;

public sealed class GenerationBenchmarkRunner(
    IGenerationModel model,
    GenerationEvaluator evaluator)
{
    private const string CandidateExecutionFailureMessage = "Candidate execution failed.";
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
                if (OpenAiBenchmarkDiagnostics.IsEnabled && exception is OpenAiAdapterException openAiException)
                {
                    OpenAiBenchmarkDiagnostics.WriteDiagnostic(openAiException.Diagnostic);
                }

                caseReports.Add(new GenerationBenchmarkCaseReport(
                    evaluationCase.Id,
                    false,
                    null,
                    exception.GetType().FullName,
                    CandidateExecutionFailureMessage));
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

internal static class OpenAiBenchmarkDiagnostics
{
    private const string EnvironmentVariable = "RJ_BENCHMARK_DIAGNOSTICS";

    public static bool IsEnabled =>
        string.Equals(Environment.GetEnvironmentVariable(EnvironmentVariable), "1", StringComparison.Ordinal)
        || string.Equals(Environment.GetEnvironmentVariable(EnvironmentVariable), "true", StringComparison.OrdinalIgnoreCase)
        || string.Equals(Environment.GetEnvironmentVariable(EnvironmentVariable), "yes", StringComparison.OrdinalIgnoreCase);

    public static void WriteDiagnostic(OpenAiAdapterDiagnostic diagnostic)
    {
        ArgumentNullException.ThrowIfNull(diagnostic);
        Console.Error.WriteLine($"OPENAI_DIAGNOSTIC stage={diagnostic.Stage}");
        Console.Error.WriteLine($"OPENAI_DIAGNOSTIC innerExceptionType={diagnostic.InnerExceptionType}");
        Console.Error.WriteLine($"OPENAI_DIAGNOSTIC httpStatus={diagnostic.HttpStatus ?? "n/a"}");
        Console.Error.WriteLine($"OPENAI_DIAGNOSTIC providerErrorCode={diagnostic.ProviderErrorCode ?? "n/a"}");
        Console.Error.WriteLine($"OPENAI_DIAGNOSTIC providerErrorMessage={diagnostic.ProviderErrorMessage}");
        Console.Error.WriteLine($"OPENAI_DIAGNOSTIC exceptionMessage={diagnostic.ExceptionMessage}");
        Console.Error.WriteLine($"OPENAI_DIAGNOSTIC modelSent={diagnostic.ModelSent}");
        Console.Error.WriteLine($"OPENAI_DIAGNOSTIC endpoint={diagnostic.Endpoint}");
        Console.Error.WriteLine($"OPENAI_DIAGNOSTIC failureClass={diagnostic.FailureClass}");
    }
}
