using System.Text.Json;
using System.Text.Json.Serialization;

namespace RJ.BenchmarkCli;

public static class BenchmarkRunManifestJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    public static string Serialize(BenchmarkRunManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        return JsonSerializer.Serialize(manifest, Options);
    }
}
