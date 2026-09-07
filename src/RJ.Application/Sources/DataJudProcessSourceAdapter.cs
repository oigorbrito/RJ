using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using RJ.Domain.Cases;

namespace RJ.Application.Sources;

public sealed class DataJudProcessSourceAdapter : IProcessSourceAdapter
{
    public const string DataJudSourceSystem = "datajud";

    public string SourceSystem => DataJudSourceSystem;

    public LegalCase Canonicalize(ProcessSourceDocument source)
    {
        ArgumentNullException.ThrowIfNull(source);

        using var document = JsonDocument.Parse(source.RawContent);
        var process = ResolveProcess(document.RootElement);
        var cnj = new LegalCaseCnj(RequiredString(process, "numeroProcesso"));
        var sourceSha256 = ComputeSha256(source.RawContent);

        return new LegalCase(
            new LegalCaseId(Path.GetFileNameWithoutExtension(source.SourceReference)),
            cnj,
            OptionalString(process, "nome") ?? $"Processo {cnj.Value}",
            GetCourt(process),
            GetPhase(process),
            OptionalString(process, "status") ?? "NAO OBSERVADO",
            TryGetDecimal(process, "valorAcao"),
            BuildParties(),
            [],
            BuildClassifications(process),
            BuildSubjects(process),
            BuildSteps(process, source.SourceName),
            [],
            BuildProvenance(source, sourceSha256));
    }

    private static JsonElement ResolveProcess(JsonElement root)
    {
        if (root.TryGetProperty("hits", out var hits) &&
            hits.TryGetProperty("hits", out var hitItems) &&
            hitItems.ValueKind == JsonValueKind.Array)
        {
            var firstHit = hitItems.EnumerateArray().FirstOrDefault();
            if (firstHit.ValueKind != JsonValueKind.Undefined && firstHit.TryGetProperty("_source", out var source))
            {
                return source;
            }
        }

        if (root.ValueKind == JsonValueKind.Array)
        {
            var first = root.EnumerateArray().FirstOrDefault();
            if (first.ValueKind != JsonValueKind.Undefined)
            {
                return first;
            }
        }

        return root;
    }

    private static LegalCaseParty[] BuildParties() =>
    [
        new("NAO OBSERVADO", "Unknown", "NAO OBSERVADO", null)
    ];

    private static LegalCaseClassification[] BuildClassifications(JsonElement process)
    {
        if (!process.TryGetProperty("classe", out var item) || item.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return [new LegalCaseClassification("NAO OBSERVADO", "NAO OBSERVADO")];
        }

        return [new LegalCaseClassification(RequiredString(item, "codigo"), RequiredString(item, "nome"))];
    }

    private static LegalCaseSubject[] BuildSubjects(JsonElement process)
    {
        if (!process.TryGetProperty("assuntos", out var subjects) || subjects.ValueKind != JsonValueKind.Array)
        {
            return [new LegalCaseSubject("NAO OBSERVADO", "NAO OBSERVADO")];
        }

        var mapped = subjects.EnumerateArray()
            .Select(item => new LegalCaseSubject(RequiredString(item, "codigo"), RequiredString(item, "nome")))
            .ToArray();

        return mapped.Length == 0 ? [new LegalCaseSubject("NAO OBSERVADO", "NAO OBSERVADO")] : mapped;
    }

    private static LegalCaseStep[] BuildSteps(JsonElement process, string sourceName)
    {
        if (!process.TryGetProperty("movimentos", out var movements) || movements.ValueKind != JsonValueKind.Array)
        {
            return [new LegalCaseStep("NAO OBSERVADO", DateTimeOffset.UnixEpoch, "NAO OBSERVADO", sourceName)];
        }

        var mapped = movements.EnumerateArray()
            .Select((item, index) => new LegalCaseStep(
                OptionalString(item, "codigo") ?? $"movimento-{index + 1}",
                ParseDateTimeOffset(OptionalString(item, "dataHora") ?? OptionalString(item, "data_hora") ?? OptionalString(item, "dataHoraMovimento") ?? "1970-01-01T00:00:00Z"),
                RequiredString(item, "nome"),
                sourceName))
            .ToArray();

        return mapped.Length == 0 ? [new LegalCaseStep("NAO OBSERVADO", DateTimeOffset.UnixEpoch, "NAO OBSERVADO", sourceName)] : mapped;
    }

    private static LegalCaseFieldProvenance[] BuildProvenance(ProcessSourceDocument source, string sourceSha256) =>
    [
        Provenance("cnj", "numeroProcesso", source, sourceSha256),
        Provenance("name", "nome|numeroProcesso", source, sourceSha256),
        Provenance("court", "orgaoJulgador.nome|tribunal", source, sourceSha256),
        Provenance("phase", "grau", source, sourceSha256),
        Provenance("status", "status", source, sourceSha256),
        Provenance("amount", "valorAcao", source, sourceSha256),
        Provenance("parties", "NAO OBSERVADO", source, sourceSha256),
        Provenance("classifications", "classe", source, sourceSha256),
        Provenance("subjects", "assuntos", source, sourceSha256),
        Provenance("steps", "movimentos", source, sourceSha256),
        Provenance("attachments", "NAO OBSERVADO", source, sourceSha256)
    ];

    private static LegalCaseFieldProvenance Provenance(
        string fieldPath,
        string observedPath,
        ProcessSourceDocument source,
        string sourceSha256) =>
        new(fieldPath, source.SourceName, source.SourceReference, sourceSha256, observedPath, source.ObservedAt);

    private static string GetCourt(JsonElement process)
    {
        if (process.TryGetProperty("orgaoJulgador", out var court) && court.ValueKind == JsonValueKind.Object)
        {
            var name = OptionalString(court, "nome");
            if (!string.IsNullOrWhiteSpace(name))
            {
                return name;
            }
        }

        return OptionalString(process, "tribunal") ?? "NAO OBSERVADO";
    }

    private static string GetPhase(JsonElement process)
    {
        if (process.TryGetProperty("grau", out var grade))
        {
            return grade.ValueKind == JsonValueKind.Number
                ? grade.GetInt32().ToString(CultureInfo.InvariantCulture)
                : RequiredString(process, "grau");
        }

        return "NAO OBSERVADO";
    }

    private static decimal? TryGetDecimal(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value) || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.Number
            ? value.GetDecimal()
            : decimal.TryParse(value.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;
    }

    private static string RequiredString(JsonElement element, string propertyName)
    {
        var value = OptionalString(element, propertyName);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"DataJud process field '{propertyName}' is required.");
        }

        return value;
    }

    private static string? OptionalString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value) || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.String
            ? value.GetString()?.Trim()
            : value.ToString().Trim();
    }

    private static DateTimeOffset ParseDateTimeOffset(string value) =>
        DateTimeOffset.Parse(value, CultureInfo.InvariantCulture);

    private static string ComputeSha256(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }
}
