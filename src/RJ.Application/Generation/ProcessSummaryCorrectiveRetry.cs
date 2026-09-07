using RJ.Application.Sources;
using RJ.Domain.Cases;

namespace RJ.Application.Generation;

public static class ProcessSummaryCorrectiveRetry
{
    public static async Task<ProcessSummaryCorrectiveRetryResult> GenerateAsync(
        GenerationService generation,
        LegalCase legalCase,
        GenerationContext context,
        LegalCaseConsistencyReport? consistencyReport,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(generation);
        ArgumentNullException.ThrowIfNull(legalCase);
        ArgumentNullException.ThrowIfNull(context);

        var firstOutput = await generation.GenerateAsync(context, cancellationToken);
        var firstValidation = ProcessSummaryValidator.Validate(legalCase, firstOutput, consistencyReport);
        if (firstValidation.IsValid)
        {
            return new ProcessSummaryCorrectiveRetryResult(firstOutput, firstValidation, 1, false);
        }

        var correctiveContext = context with
        {
            Query = BuildCorrectiveQuery(context.Query, firstValidation.Errors)
        };
        var secondOutput = await generation.GenerateAsync(correctiveContext, cancellationToken);
        var secondValidation = ProcessSummaryValidator.Validate(legalCase, secondOutput, consistencyReport);

        return new ProcessSummaryCorrectiveRetryResult(secondOutput, secondValidation, 2, true);
    }

    private static string BuildCorrectiveQuery(string originalQuery, IReadOnlyList<string> errors)
    {
        var sanitizedErrors = errors
            .Select(SanitizeError)
            .Where(error => !string.IsNullOrWhiteSpace(error))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(error => error, StringComparer.Ordinal);

        return string.Join(
            Environment.NewLine,
            originalQuery,
            "CorrectiveRetry: regenerate using only the supplied context evidence and fix these validator failures.",
            $"ValidationErrors: {string.Join("; ", sanitizedErrors)}");
    }

    private static string SanitizeError(string error)
    {
        if (string.IsNullOrWhiteSpace(error))
        {
            return string.Empty;
        }

        return error
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Trim();
    }
}

public sealed record ProcessSummaryCorrectiveRetryResult(
    GenerationModelOutput Output,
    ProcessSummaryValidationResult Validation,
    int Attempts,
    bool Retried);
