using System.Text.Json;

namespace RJ.Application.Benchmarking;

public static class GenerationBenchmarkJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public static string Serialize(GenerationBenchmarkReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        return JsonSerializer.Serialize(report, Options);
    }

    public static GenerationBenchmarkReport Parse(ReadOnlySpan<byte> utf8Json)
    {
        if (utf8Json.IsEmpty)
        {
            throw new ArgumentException("Generation benchmark report cannot be empty.", nameof(utf8Json));
        }

        return JsonSerializer.Deserialize<GenerationBenchmarkReport>(utf8Json, Options)
            ?? throw new InvalidOperationException("Generation benchmark report produced no document.");
    }
}
