namespace RJ.Domain.Cases;

public sealed record LegalCaseCnj
{
    public LegalCaseCnj(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Legal case CNJ cannot be empty.", nameof(value));
        }

        var digits = OnlyDigits(value);
        if (digits.Length != 20)
        {
            throw new ArgumentException("Legal case CNJ must contain exactly 20 digits.", nameof(value));
        }

        if (!HasValidCheckDigits(digits))
        {
            throw new ArgumentException("Legal case CNJ check digits are invalid.", nameof(value));
        }

        Digits = digits;
        Value = Format(digits);
    }

    public string Value { get; }

    public string Digits { get; }

    public override string ToString() => Value;

    private static string OnlyDigits(string value)
    {
        var digits = new char[value.Length];
        var count = 0;
        foreach (var character in value)
        {
            if (char.IsDigit(character))
            {
                digits[count] = character;
                count++;
            }
            else if (!char.IsWhiteSpace(character) && character != '-' && character != '.')
            {
                throw new ArgumentException("Legal case CNJ contains invalid characters.", nameof(value));
            }
        }

        return new string(digits, 0, count);
    }

    private static bool HasValidCheckDigits(string digits)
    {
        var orderedDigits = string.Create(20, digits, static (span, source) =>
        {
            source.AsSpan(0, 7).CopyTo(span);
            source.AsSpan(9, 4).CopyTo(span[7..]);
            source.AsSpan(13, 1).CopyTo(span[11..]);
            source.AsSpan(14, 2).CopyTo(span[12..]);
            source.AsSpan(16, 4).CopyTo(span[14..]);
            source.AsSpan(7, 2).CopyTo(span[18..]);
        });

        var remainder = 0;
        foreach (var digit in orderedDigits)
        {
            remainder = ((remainder * 10) + digit - '0') % 97;
        }

        return remainder == 1;
    }

    private static string Format(string digits) =>
        string.Create(25, digits, static (span, source) =>
        {
            source.AsSpan(0, 7).CopyTo(span);
            span[7] = '-';
            source.AsSpan(7, 2).CopyTo(span[8..]);
            span[10] = '.';
            source.AsSpan(9, 4).CopyTo(span[11..]);
            span[15] = '.';
            source.AsSpan(13, 1).CopyTo(span[16..]);
            span[17] = '.';
            source.AsSpan(14, 2).CopyTo(span[18..]);
            span[20] = '.';
            source.AsSpan(16, 4).CopyTo(span[21..]);
        });
}
