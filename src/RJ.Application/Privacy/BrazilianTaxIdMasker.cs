using System.Text.RegularExpressions;

namespace RJ.Application.Privacy;

public static partial class BrazilianTaxIdMasker
{
    public static string Mask(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        var withoutCnpj = CnpjPattern().Replace(
            value,
            static match => IsValidCnpj(match.Value) ? MaskDigits(match.Value) : match.Value);

        return CpfPattern().Replace(
            withoutCnpj,
            static match => IsValidCpf(match.Value) ? MaskDigits(match.Value) : match.Value);
    }

    private static string MaskDigits(string value)
    {
        var buffer = value.ToCharArray();
        for (var index = 0; index < buffer.Length; index++)
        {
            if (char.IsAsciiDigit(buffer[index]))
            {
                buffer[index] = '*';
            }
        }

        return new string(buffer);
    }

    private static bool IsValidCpf(string value)
    {
        Span<int> digits = stackalloc int[11];
        if (!TryExtractDigits(value, digits) || AllDigitsEqual(digits))
        {
            return false;
        }

        var first = CalculateCpfDigit(digits[..9], 10);
        var second = CalculateCpfDigit(digits[..10], 11);
        return digits[9] == first && digits[10] == second;
    }

    private static bool IsValidCnpj(string value)
    {
        Span<int> digits = stackalloc int[14];
        if (!TryExtractDigits(value, digits) || AllDigitsEqual(digits))
        {
            return false;
        }

        ReadOnlySpan<int> firstWeights = [5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2];
        ReadOnlySpan<int> secondWeights = [6, 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2];

        var first = CalculateCnpjDigit(digits[..12], firstWeights);
        var second = CalculateCnpjDigit(digits[..13], secondWeights);
        return digits[12] == first && digits[13] == second;
    }

    private static int CalculateCpfDigit(ReadOnlySpan<int> digits, int initialWeight)
    {
        var sum = 0;
        for (var index = 0; index < digits.Length; index++)
        {
            sum += digits[index] * (initialWeight - index);
        }

        var remainder = (sum * 10) % 11;
        return remainder == 10 ? 0 : remainder;
    }

    private static int CalculateCnpjDigit(ReadOnlySpan<int> digits, ReadOnlySpan<int> weights)
    {
        var sum = 0;
        for (var index = 0; index < digits.Length; index++)
        {
            sum += digits[index] * weights[index];
        }

        var remainder = sum % 11;
        return remainder < 2 ? 0 : 11 - remainder;
    }

    private static bool TryExtractDigits(string value, Span<int> destination)
    {
        var destinationIndex = 0;
        foreach (var character in value)
        {
            if (!char.IsAsciiDigit(character))
            {
                continue;
            }

            if (destinationIndex >= destination.Length)
            {
                return false;
            }

            destination[destinationIndex++] = character - '0';
        }

        return destinationIndex == destination.Length;
    }

    private static bool AllDigitsEqual(ReadOnlySpan<int> digits)
    {
        for (var index = 1; index < digits.Length; index++)
        {
            if (digits[index] != digits[0])
            {
                return false;
            }
        }

        return true;
    }

    [GeneratedRegex(@"(?<!\d)(?:\d{14}|\d{2}\.\d{3}\.\d{3}/\d{4}-\d{2})(?!\d)", RegexOptions.CultureInvariant)]
    private static partial Regex CnpjPattern();

    [GeneratedRegex(@"(?<!\d)(?:\d{11}|\d{3}\.\d{3}\.\d{3}-\d{2})(?!\d)", RegexOptions.CultureInvariant)]
    private static partial Regex CpfPattern();
}
