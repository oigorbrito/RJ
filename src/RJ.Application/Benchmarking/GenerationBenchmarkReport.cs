using RJ.Application.Evaluation;

namespace RJ.Application.Benchmarking;

public sealed record GenerationBenchmarkCaseReport(
    string CaseId,
    bool Passed,
    GenerationEvaluationResult? Evaluation,
    string? ErrorType,
    string? ErrorMessage);

public sealed record GenerationBenchmarkReport(
    GenerationBenchmarkMetadata Metadata,
    int TotalCases,
    int PassedCases,
    int FailedCases,
    double MinimumClaimRecall,
    double MinimumCitationValidity,
    double MinimumGroundedness,
    bool Passed,
    IReadOnlyList<GenerationBenchmarkCaseReport> Cases);
