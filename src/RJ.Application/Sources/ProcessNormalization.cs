using System.Globalization;
using System.Text.RegularExpressions;

namespace RJ.Application.Sources;

public static partial class ProcessNormalization
{
    public static string NormalizeStepContent(string value)
    {
        var required = Required(value, nameof(value));
        return WhitespacePattern().Replace(required, " ");
    }

    public static ExplicitProcessCorrection CorrectObservedValue(string rawValue, string normalizedValue, string reason) =>
        new(rawValue, normalizedValue, reason);

    public static string FormatSaoPauloDateTime(DateTimeOffset instant)
    {
        var timeZone = FindSaoPauloTimeZone();
        var local = TimeZoneInfo.ConvertTime(instant, timeZone);
        return local.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture);
    }

    private static TimeZoneInfo FindSaoPauloTimeZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById("E. South America Standard Time");
        }
    }

    private static string Required(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Value cannot be empty.", parameterName);
        }

        return value.Trim();
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespacePattern();
}

public sealed record ExplicitProcessCorrection(string RawValue, string NormalizedValue, string Reason)
{
    public string RawValue { get; } = Required(RawValue, nameof(RawValue));

    public string NormalizedValue { get; } = Required(NormalizedValue, nameof(NormalizedValue));

    public string Reason { get; } = Required(Reason, nameof(Reason));

    private static string Required(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Value cannot be empty.", parameterName);
        }

        return value.Trim();
    }
}
