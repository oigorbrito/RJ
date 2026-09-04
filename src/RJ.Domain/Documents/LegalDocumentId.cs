namespace RJ.Domain.Documents;

public readonly record struct LegalDocumentId
{
    public LegalDocumentId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Legal document identifier cannot be empty.", nameof(value));
        }

        Value = value.Trim();
    }

    public string Value { get; }

    public override string ToString() => Value;
}
