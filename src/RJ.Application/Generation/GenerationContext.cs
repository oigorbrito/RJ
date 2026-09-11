namespace RJ.Application.Generation;

public sealed record GenerationContext(
    string CaseId,
    string Query,
    int CharacterBudget,
    int UsedCharacters,
    IReadOnlyList<GenerationContextItem> Items)
{
    public string CaseId { get; } = Require(CaseId, nameof(CaseId));

    public string Query { get; } = Require(Query, nameof(Query));

    public int CharacterBudget { get; } = RequirePositive(CharacterBudget, nameof(CharacterBudget));

    public int UsedCharacters { get; } = RequireUsedCharacters(UsedCharacters, CharacterBudget, nameof(UsedCharacters));

    public IReadOnlyList<GenerationContextItem> Items { get; } = RequireItems(Items, CaseId);

    public GenerationContext WithQuery(string query) =>
        new(CaseId, query, CharacterBudget, UsedCharacters, Items);

    private static string Require(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Generation context value cannot be empty.", parameterName);
        }

        return value.Trim();
    }

    private static int RequirePositive(int value, string parameterName)
    {
        if (value <= 0)
        {
            throw new ArgumentOutOfRangeException(parameterName, value, "Generation context budget must be positive.");
        }

        return value;
    }

    private static int RequireUsedCharacters(int value, int characterBudget, string parameterName)
    {
        if (value < 0 || value > characterBudget)
        {
            throw new ArgumentOutOfRangeException(parameterName, value, "Generation context used characters must fit within the character budget.");
        }

        return value;
    }

    private static GenerationContextItem[] RequireItems(IReadOnlyList<GenerationContextItem> value, string caseId)
    {
        ArgumentNullException.ThrowIfNull(value);

        var normalizedCaseId = Require(caseId, nameof(caseId));
        var items = value.ToArray();
        if (items.Any(item => item is null))
        {
            throw new ArgumentException("Generation context cannot contain null items.", nameof(value));
        }

        if (items.Any(item => !StringComparer.Ordinal.Equals(item.CaseId, normalizedCaseId)))
        {
            throw new ArgumentException("Generation context items must belong to the context case.", nameof(value));
        }

        return items;
    }
}
