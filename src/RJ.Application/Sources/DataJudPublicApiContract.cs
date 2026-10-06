using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace RJ.Application.Sources;

public static class DataJudPublicApiContract
{
    public const string SourceSystem = "datajud-public-api";
    public const string BaseUrl = "https://api-publica.datajud.cnj.jus.br";

    public static Uri SearchEndpoint(string tribunalAlias)
    {
        var alias = RequireAlias(tribunalAlias);
        return new Uri($"{BaseUrl}/api_publica_{alias}/_search", UriKind.Absolute);
    }

    public static string BuildProcessNumberQuery(string cnj)
    {
        var normalized = new string((cnj ?? string.Empty).Where(char.IsDigit).ToArray());
        if (normalized.Length != 20)
        {
            throw new ArgumentException("DataJud process-number query requires a 20-digit CNJ number.", nameof(cnj));
        }

        return JsonSerializer.Serialize(new
        {
            query = new
            {
                match = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["numeroProcesso"] = normalized
                }
            }
        });
    }

    public static DataJudProcessObservation ParseSingleProcessResponse(
        ProcessSourceDocument source,
        string expectedCnj)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!StringComparer.Ordinal.Equals(source.SourceSystem, SourceSystem))
        {
            throw new ArgumentException("Process source is not a DataJud public API document.", nameof(source));
        }

        var expected = NormalizeCnj(expectedCnj);
        using var document = JsonDocument.Parse(source.RawContent);
        var root = document.RootElement;
        var hits = RequiredObject(root, "hits");
        var items = RequiredArray(hits, "hits").ToArray();
        if (items.Length != 1)
        {
            throw new ArgumentException("DataJud response must contain exactly one process hit for canonical enrichment.", nameof(source));
        }

        var payload = RequiredObject(items[0], "_source");
        var observedCnj = NormalizeCnj(RequiredString(payload, "numeroProcesso"));
        if (!StringComparer.Ordinal.Equals(observedCnj, expected))
        {
            throw new InvalidOperationException("DataJud response CNJ does not match the requested process.");
        }

        var tribunal = RequiredString(payload, "tribunal");
        var degree = RequiredString(payload, "grau");
        var secrecyLevel = RequiredInt64(payload, "nivelSigilo");
        var court = ParseCourt(payload);
        var classification = ParseCodeName(RequiredObject(payload, "classe"), "classe");
        var subjects = RequiredArray(payload, "assuntos")
            .Select(item => ParseCodeName(item, "assuntos"))
            .OrderBy(item => item.Code)
            .ThenBy(item => item.Name, StringComparer.Ordinal)
            .ToArray();
        var movements = RequiredArray(payload, "movimentos")
            .Select(ParseMovement)
            .OrderBy(item => item.OccurredAt)
            .ThenBy(item => item.Code)
            .ThenBy(item => item.Name, StringComparer.Ordinal)
            .ToArray();

        return new DataJudProcessObservation(
            observedCnj,
            tribunal,
            degree,
            secrecyLevel,
            court,
            classification,
            subjects,
            movements,
            source.SourceReference,
            ComputeSha256(source.RawContent),
            source.ObservedAt);
    }

    private static DataJudMovementObservation ParseMovement(JsonElement movement)
    {
        var code = RequiredInt64(movement, "codigo").ToString(System.Globalization.CultureInfo.InvariantCulture);
        var name = RequiredString(movement, "nome");
        var occurredAt = DateTimeOffset.Parse(
            RequiredString(movement, "dataHora"),
            System.Globalization.CultureInfo.InvariantCulture);
        string? courtName = null;
        if (movement.TryGetProperty("orgaoJulgador", out var court) && court.ValueKind == JsonValueKind.Object)
        {
            courtName = OptionalString(court, "nomeOrgao");
        }

        return new DataJudMovementObservation(code, name, occurredAt, courtName);
    }

    private static DataJudCodeName ParseCodeName(JsonElement element, string context) =>
        new(
            RequiredInt64(element, "codigo").ToString(System.Globalization.CultureInfo.InvariantCulture),
            RequiredString(element, "nome"));

    private static DataJudCourtObservation ParseCourt(JsonElement payload)
    {
        var court = RequiredObject(payload, "orgaoJulgador");
        return new DataJudCourtObservation(
            RequiredInt64(court, "codigo").ToString(System.Globalization.CultureInfo.InvariantCulture),
            RequiredString(court, "nome"),
            OptionalInt64(court, "codigoMunicipioIBGE"));
    }

    private static string NormalizeCnj(string value)
    {
        var digits = new string((value ?? string.Empty).Where(char.IsDigit).ToArray());
        if (digits.Length != 20)
        {
            throw new ArgumentException("CNJ number must contain exactly 20 digits.", nameof(value));
        }

        return digits;
    }

    private static string RequireAlias(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("DataJud tribunal alias cannot be empty.", nameof(value));
        }

        var alias = value.Trim().ToLowerInvariant();
        if (alias.Any(character => !(char.IsLetterOrDigit(character) || character == '-')))
        {
            throw new ArgumentException("DataJud tribunal alias contains unsupported characters.", nameof(value));
        }

        return alias;
    }

    private static JsonElement RequiredObject(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value) || value.ValueKind != JsonValueKind.Object)
        {
            throw InvalidSchema(propertyName);
        }

        return value;
    }

    private static JsonElement.ArrayEnumerator RequiredArray(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value) || value.ValueKind != JsonValueKind.Array)
        {
            throw InvalidSchema(propertyName);
        }

        return value.EnumerateArray();
    }

    private static string RequiredString(JsonElement element, string propertyName)
    {
        var value = OptionalString(element, propertyName);
        return string.IsNullOrWhiteSpace(value) ? throw InvalidSchema(propertyName) : value;
    }

    private static string? OptionalString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value) || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        if (value.ValueKind != JsonValueKind.String)
        {
            throw InvalidSchema(propertyName);
        }

        return value.GetString()?.Trim();
    }

    private static long RequiredInt64(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value)
            || value.ValueKind != JsonValueKind.Number
            || !value.TryGetInt64(out var result))
        {
            throw InvalidSchema(propertyName);
        }

        return result;
    }

    private static long? OptionalInt64(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value) || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt64(out var result))
        {
            throw InvalidSchema(propertyName);
        }

        return result;
    }

    private static ArgumentException InvalidSchema(string propertyName) =>
        new("DataJud public API response schema is invalid for the documented contract.", propertyName);

    private static string ComputeSha256(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}

public sealed record DataJudProcessObservation(
    string Cnj,
    string Tribunal,
    string Degree,
    long SecrecyLevel,
    DataJudCourtObservation Court,
    DataJudCodeName Classification,
    IReadOnlyList<DataJudCodeName> Subjects,
    IReadOnlyList<DataJudMovementObservation> Movements,
    string SourceReference,
    string SourceSha256,
    DateTimeOffset ObservedAt);

public sealed record DataJudCourtObservation(string Code, string Name, long? MunicipalityIbgeCode);

public sealed record DataJudCodeName(string Code, string Name);

public sealed record DataJudMovementObservation(string Code, string Name, DateTimeOffset OccurredAt, string? CourtName);
