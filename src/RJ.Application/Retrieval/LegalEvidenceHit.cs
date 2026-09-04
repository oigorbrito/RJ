namespace RJ.Application.Retrieval;

public sealed record LegalEvidenceHit(
    string CaseId,
    string DocumentId,
    string SourceName,
    string ContentSha256,
    string Excerpt,
    SourcePosition Position,
    float Rank);
