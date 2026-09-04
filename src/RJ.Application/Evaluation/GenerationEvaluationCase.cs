using RJ.Application.Generation;

namespace RJ.Application.Evaluation;

public sealed record GenerationEvaluationCase(
    string Id,
    GenerationContext Context,
    IReadOnlyList<ExpectedGenerationClaim> ExpectedClaims,
    bool ExpectAbstention);

public sealed record ExpectedGenerationClaim(
    string Text,
    IReadOnlyList<GenerationCitation> Citations);
