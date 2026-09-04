namespace RJ.Application.Generation;

public sealed record GenerationModelOutput(
    bool Abstained,
    string? AbstentionReason,
    IReadOnlyList<GenerationClaim> Claims);
