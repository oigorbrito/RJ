namespace RJ.Application.Evaluation;

public sealed record GenerationEvaluationResult(
    string CaseId,
    bool Abstained,
    int ExpectedClaims,
    int ActualClaims,
    double ClaimRecall,
    double CitationValidity,
    double Groundedness,
    bool Passed);
