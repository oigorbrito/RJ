using RJ.Application.Retrieval;

namespace RJ.Application.Generation;

public sealed record GenerationContextItem(
    string CaseId,
    string DocumentId,
    string SourceName,
    string ContentSha256,
    string Excerpt,
    SourcePosition Position,
    float Rank);
