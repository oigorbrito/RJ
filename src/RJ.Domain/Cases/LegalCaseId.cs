namespace RJ.Domain.Cases;

public sealed record LegalCaseId
{
    public LegalCaseId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Legal case identifier cannot be empty.", nameof(value));
        }

        Value = value.Trim();
    }

    public string Value { get; }

    public override string ToString() => Value;
}
