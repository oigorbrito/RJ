using System.Text.Json;

namespace RJ.Application.Benchmarking;

public static class RetrievalBenchmarkCatalogJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public static string Serialize(RetrievalBenchmarkCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        catalog.Validate();
        return JsonSerializer.Serialize(catalog, Options);
    }

    public static RetrievalBenchmarkCatalog Parse(ReadOnlySpan<byte> utf8Json)
    {
        if (utf8Json.IsEmpty)
        {
            throw new ArgumentException("Retrieval benchmark catalog cannot be empty.", nameof(utf8Json));
        }

        var catalog = JsonSerializer.Deserialize<RetrievalBenchmarkCatalog>(utf8Json, Options)
            ?? throw new InvalidOperationException("Retrieval benchmark catalog produced no document.");
        catalog.Validate();
        return catalog;
    }
}
