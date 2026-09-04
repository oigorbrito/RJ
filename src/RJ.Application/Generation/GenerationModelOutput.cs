namespace RJ.Application.Generation;

public sealed record GenerationModelOutput(
    IReadOnlyList<GenerationClaim> Claims);
