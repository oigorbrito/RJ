namespace RJ.Application.Rag;

public sealed record LocalRagEvaluationResult(
    string Strategy,
    int QueryCount,
    int HitAt1,
    int HitAt3,
    int HitAt5,
    double Mrr,
    TimeSpan Duration,
    IReadOnlyList<LocalRagPerQueryResult> PerQueryResults);

public sealed record LocalRagPerQueryResult(
    string QueryId,
    string Question,
    string ExpectedAnswer,
    string OraclePath,
    string? MatchedChunkId,
    string? MatchedSource,
    int? Rank,
    string Status);
