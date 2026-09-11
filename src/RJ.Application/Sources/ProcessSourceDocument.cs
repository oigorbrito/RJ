namespace RJ.Application.Sources;

public sealed record ProcessSourceDocument(
    string SourceSystem,
    string SourceName,
    string SourceReference,
    string RawContent,
    DateTimeOffset ObservedAt)
{
    public string SourceSystem { get; } = RequireSourceSystem(SourceSystem);

    public string SourceName { get; } = Require(SourceName, nameof(SourceName));

    public string SourceReference { get; } = Require(SourceReference, nameof(SourceReference));

    public string RawContent { get; } = Require(RawContent, nameof(RawContent));

    public DateTimeOffset ObservedAt { get; } = RequireObservedAt(ObservedAt, nameof(ObservedAt));

    internal static string RequireSourceSystem(string value) => Require(value, nameof(SourceSystem));

    private static DateTimeOffset RequireObservedAt(DateTimeOffset value, string parameterName)
    {
        if (value == default)
        {
            throw new ArgumentException("Process source observed instant cannot be empty.", parameterName);
        }

        return value;
    }

    private static string Require(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Process source value cannot be empty.", parameterName);
        }

        return value.Trim();
    }
}
