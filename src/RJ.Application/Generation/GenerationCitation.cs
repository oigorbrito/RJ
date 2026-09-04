namespace RJ.Application.Generation;

public sealed record GenerationCitation(
    string DocumentId,
    string ContentSha256,
    int StartOffset,
    int Length);
