using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using RJ.Domain.Cases;

namespace RJ.Application.Sources;

public sealed class JuditProcessSourceAdapter : IProcessSourceAdapter
{
    public const string JuditSourceSystem = "judit";

    public string SourceSystem => JuditSourceSystem;

    public LegalCase Canonicalize(ProcessSourceDocument source)
    {
        ArgumentNullException.ThrowIfNull(source);

        using var document = JsonDocument.Parse(source.RawContent);
        var lawsuitPage = RequiredArray(document.RootElement, "page_data")
            .FirstOrDefault(page => StringComparer.Ordinal.Equals(OptionalString(page, "response_type"), "lawsuit"));

        if (lawsuitPage.ValueKind is JsonValueKind.Undefined)
        {
            throw new ArgumentException("Judit process source schema is invalid.", nameof(source));
        }

        var response = RequiredObject(lawsuitPage, "response_data");
        var sourceSha256 = ComputeSha256(source.RawContent);
        var sourceName = GetCrawlerSourceName(response) ?? source.SourceName;

        return new LegalCase(
            new LegalCaseId(Path.GetFileNameWithoutExtension(source.SourceReference)),
            new LegalCaseCnj(RequiredString(response, "code")),
            RequiredString(response, "name"),
            GetCourt(response),
            RequiredString(response, "phase"),
            RequiredString(response, "status"),
            RequiredInt32(response, "secrecy_level"),
            OptionalDecimal(response, "amount"),
            BuildParties(response),
            BuildLawyers(response),
            BuildClassifications(response),
            BuildSubjects(response),
            BuildSteps(response),
            BuildAttachments(response),
            BuildProvenance(source, sourceName, sourceSha256));
    }

    private static LegalCaseParty[] BuildParties(JsonElement response) =>
        RequiredArray(response, "parties")
            .Select(party => new LegalCaseParty(
                RequiredString(party, "name"),
                RequiredString(party, "side"),
                RequiredString(party, "person_type"),
                BrazilianDocumentMasker.MaskCpfCnpj(OptionalString(party, "main_document"))))
            .ToArray();

    private static LegalCaseLawyer[] BuildLawyers(JsonElement response)
    {
        var lawyers = new Dictionary<string, LegalCaseLawyer>(StringComparer.Ordinal);

        foreach (var party in RequiredArray(response, "parties"))
        {
            AddLawyerDocuments(
                lawyers,
                OptionalString(party, "name"),
                party.TryGetProperty("documents", out var partyDocuments) && partyDocuments.ValueKind == JsonValueKind.Array
                    ? partyDocuments.EnumerateArray()
                    : []);

            if (!party.TryGetProperty("lawyers", out var partyLawyers))
            {
                continue;
            }

            foreach (var lawyer in RequiredArray(party, "lawyers"))
            {
                AddLawyerDocuments(lawyers, RequiredString(lawyer, "name"), RequiredArray(lawyer, "documents"));
            }
        }

        return lawyers.Values.ToArray();
    }

    private static void AddLawyerDocuments(
        Dictionary<string, LegalCaseLawyer> lawyers,
        string? lawyerName,
        IEnumerable<JsonElement> documents)
    {
        if (string.IsNullOrWhiteSpace(lawyerName))
        {
            return;
        }

        foreach (var document in documents)
        {
            if (!StringComparer.OrdinalIgnoreCase.Equals(OptionalString(document, "document_type"), "oab"))
            {
                continue;
            }

            var oab = RequiredString(document, "document");
            lawyers.TryAdd(oab, new LegalCaseLawyer(lawyerName, oab));
        }
    }

    private static LegalCaseClassification[] BuildClassifications(JsonElement response) =>
        RequiredArray(response, "classifications")
            .Select(item => new LegalCaseClassification(RequiredString(item, "code"), RequiredString(item, "name")))
            .ToArray();

    private static LegalCaseSubject[] BuildSubjects(JsonElement response) =>
        RequiredArray(response, "subjects")
            .Select(item => new LegalCaseSubject(RequiredString(item, "code"), RequiredString(item, "name")))
            .ToArray();

    private static LegalCaseStep[] BuildSteps(JsonElement response) =>
        RequiredArray(response, "steps")
            .Select(step => new LegalCaseStep(
                RequiredString(step, "step_id"),
                ParseDateTimeOffset(RequiredString(step, "step_date")),
                RequiredString(step, "content"),
                RequiredString(step, "source_name")))
            .ToArray();

    private static LegalCaseAttachment[] BuildAttachments(JsonElement response) =>
        RequiredArray(response, "attachments")
            .Select(attachment => new LegalCaseAttachment(
                RequiredString(attachment, "attachment_id"),
                RequiredString(attachment, "attachment_name"),
                RequiredString(attachment, "step_id"),
                RequiredString(attachment, "extension"),
                RequiredString(attachment, "status"),
                ParseDateTimeOffset(RequiredString(attachment, "attachment_date"))))
            .ToArray();

    private static LegalCaseFieldProvenance[] BuildProvenance(ProcessSourceDocument source, string sourceName, string sourceSha256) =>
    [
        Provenance("cnj", "page_data[0].response_data.code", source, sourceName, sourceSha256),
        Provenance("name", "page_data[0].response_data.name", source, sourceName, sourceSha256),
        Provenance("court", "page_data[0].response_data.courts", source, sourceName, sourceSha256),
        Provenance("phase", "page_data[0].response_data.phase", source, sourceName, sourceSha256),
        Provenance("status", "page_data[0].response_data.status", source, sourceName, sourceSha256),
        Provenance("secrecy_level", "page_data[0].response_data.secrecy_level", source, sourceName, sourceSha256),
        Provenance("amount", "page_data[0].response_data.amount", source, sourceName, sourceSha256),
        Provenance("parties", "page_data[0].response_data.parties", source, sourceName, sourceSha256),
        Provenance("lawyers", "page_data[0].response_data.parties[].lawyers", source, sourceName, sourceSha256),
        Provenance("classifications", "page_data[0].response_data.classifications", source, sourceName, sourceSha256),
        Provenance("subjects", "page_data[0].response_data.subjects", source, sourceName, sourceSha256),
        Provenance("steps", "page_data[0].response_data.steps", source, sourceName, sourceSha256),
        Provenance("attachments", "page_data[0].response_data.attachments", source, sourceName, sourceSha256)
    ];

    private static LegalCaseFieldProvenance Provenance(
        string fieldPath,
        string observedPath,
        ProcessSourceDocument source,
        string sourceName,
        string sourceSha256) =>
        new(fieldPath, sourceName, source.SourceReference, sourceSha256, observedPath, source.ObservedAt);

    private static string GetCourt(JsonElement response)
    {
        var courts = RequiredArray(response, "courts").ToArray();
        return courts.Length > 0
            ? RequiredString(courts[0], "name")
            : RequiredString(response, "county");
    }

    private static string? GetCrawlerSourceName(JsonElement response) =>
        response.TryGetProperty("crawler", out var crawler) ? OptionalString(crawler, "source_name") : null;

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
        if (string.IsNullOrWhiteSpace(value))
        {
            throw InvalidSchema(propertyName);
        }

        return value;
    }

    private static string? OptionalString(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw InvalidSchema(propertyName);
        }

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

    private static decimal? OptionalDecimal(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value) || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        if (value.ValueKind != JsonValueKind.Number || !value.TryGetDecimal(out var amount))
        {
            throw InvalidSchema(propertyName);
        }

        return amount;
    }

    private static int RequiredInt32(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value)
            || value.ValueKind != JsonValueKind.Number
            || !value.TryGetInt32(out var result))
        {
            throw InvalidSchema(propertyName);
        }

        return result;
    }

    private static ArgumentException InvalidSchema(string propertyName) =>
        new("Judit process source schema is invalid.", propertyName);

    private static DateTimeOffset ParseDateTimeOffset(string value) =>
        DateTimeOffset.Parse(value, CultureInfo.InvariantCulture);

    private static string ComputeSha256(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }
}
