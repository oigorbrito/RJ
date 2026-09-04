namespace RJ.Application.Generation;

public sealed record GenerationClaim(
    string Text,
    IReadOnlyList<GenerationCitation> Citations);
