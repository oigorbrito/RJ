using System.Text;

namespace RJ.Application.Normalization;

public static class ProcessTextNormalizer
{
    public static string Normalize(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        var canonicalUnicode = value.Normalize(NormalizationForm.FormC);
        var canonicalLineEndings = canonicalUnicode
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');

        var lines = canonicalLineEndings.Split('\n');
        for (var index = 0; index < lines.Length; index++)
        {
            lines[index] = lines[index].TrimEnd();
        }

        var firstContentLine = 0;
        while (firstContentLine < lines.Length && lines[firstContentLine].Length == 0)
        {
            firstContentLine++;
        }

        var lastContentLine = lines.Length - 1;
        while (lastContentLine >= firstContentLine && lines[lastContentLine].Length == 0)
        {
            lastContentLine--;
        }

        if (firstContentLine > lastContentLine)
        {
            return string.Empty;
        }

        return string.Join('\n', lines[firstContentLine..(lastContentLine + 1)]);
    }
}
