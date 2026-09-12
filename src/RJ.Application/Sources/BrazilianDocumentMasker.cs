namespace RJ.Application.Sources;

public static class BrazilianDocumentMasker
{
    public static string? MaskCpfCnpj(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var digits = DigitsOnly(value);
        return digits.Length switch
        {
            11 => $"***.{digits[3..6]}.{digits[6..9]}-**",
            14 => $"**.{digits[2..5]}.{digits[5..8]}/{digits[8..12]}-**",
            _ => throw new ArgumentException("Brazilian document must be a CPF or CNPJ.", nameof(value))
        };
    }

    private static string DigitsOnly(string value)
    {
        var digits = new char[value.Length];
        var count = 0;

        foreach (var character in value)
        {
            if (character is >= '0' and <= '9')
            {
                digits[count] = character;
                count++;
            }
        }

        return new string(digits, 0, count);
    }
}
