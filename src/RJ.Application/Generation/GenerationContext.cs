namespace RJ.Application.Generation;

public sealed record GenerationContext(
    string CaseId,
    string Query,
    int CharacterBudget,
    int UsedCharacters,
    IReadOnlyList<GenerationContextItem> Items);
