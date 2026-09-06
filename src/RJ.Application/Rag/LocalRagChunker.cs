using System.Text.Json;

namespace RJ.Application.Rag;

public sealed class LocalRagChunker
{
    #pragma warning disable CA1822
    public IReadOnlyList<LocalRagChunk> BuildChunks(JsonDocument fixture, string fixturePath)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        ArgumentException.ThrowIfNullOrWhiteSpace(fixturePath);

        var chunks = new List<LocalRagChunk>();
        var pageData = fixture.RootElement.GetProperty("page_data");

        foreach (var page in pageData.EnumerateArray())
        {
            var responseId = page.GetProperty("response_id").GetRawText().Trim('"');
            var responseType = page.GetProperty("response_type").GetString() ?? string.Empty;
            if (responseType.Equals("lawsuit", StringComparison.Ordinal))
            {
                chunks.AddRange(BuildLawsuitChunks(page, fixturePath, responseId, responseType));
            }
            else if (responseType.Equals("summary", StringComparison.Ordinal))
            {
                chunks.Add(BuildSummaryChunk(page, fixturePath, responseId, responseType));
            }
        }

        return chunks;
    }
    #pragma warning restore CA1822

    private static IEnumerable<LocalRagChunk> BuildLawsuitChunks(JsonElement page, string fixturePath, string responseId, string responseType)
    {
        var response = page.GetProperty("response_data");
        var caseId = response.GetProperty("code").GetString() ?? string.Empty;
        var metadata = BuildMetadata(page, response);

        yield return Chunk(caseId, responseId, responseType, fixturePath, "overview",
            string.Join("\n", new[]
            {
                $"processo {response.GetProperty("code").GetString()}",
                $"nome {response.GetProperty("name").GetString()}",
                $"tribunal {response.GetProperty("tribunal").GetString()}",
                $"comarca {response.GetProperty("county").GetString()}",
                $"cidade {response.GetProperty("city").GetString()}",
                $"estado {response.GetProperty("state").GetString()}",
                $"area {response.GetProperty("area").GetString()}",
                $"valor {response.GetProperty("amount").GetRawText()}",
                $"juiz {response.GetProperty("judge").GetString()}",
                $"status {response.GetProperty("status").GetString()}",
                $"fase {response.GetProperty("phase").GetString()}"
            }.Where(value => !string.IsNullOrWhiteSpace(value))), metadata);

        yield return Chunk(caseId, responseId, responseType, fixturePath, "parties",
            string.Join("\n", response.GetProperty("parties").EnumerateArray().Select(party =>
            {
                var parts = new[]
                {
                    $"parte {party.GetProperty("name").GetString()}",
                    $"partes {party.GetProperty("name").GetString()}",
                    $"tipo {party.GetProperty("person_type").GetString()}",
                    $"autores reu advogado",
                    party.TryGetProperty("main_document", out var main) ? $"documento {main.GetRawText()}" : null,
                    party.TryGetProperty("documents", out var documents) ? string.Join(" ", documents.EnumerateArray().Select(d => $"doc {d.GetProperty("document").GetString()} {d.GetProperty("document_type").GetString()}")) : null,
                    party.TryGetProperty("lawyers", out var lawyers) ? string.Join(" ", lawyers.EnumerateArray().Select(l => $"advogado {l.GetProperty("name").GetString()} {string.Join(" ", l.GetProperty("documents").EnumerateArray().Select(d => d.GetProperty("document").GetString()))}")) : null
                };
                return string.Join(" ", parts.Where(value => !string.IsNullOrWhiteSpace(value)));
            })), metadata);

        yield return Chunk(caseId, responseId, responseType, fixturePath, "classifications",
            string.Join("\n", response.GetProperty("classifications").EnumerateArray().Select(item => $"classificacao classificacoes {item.GetProperty("name").GetString()}")), metadata);

        yield return Chunk(caseId, responseId, responseType, fixturePath, "subjects",
            string.Join("\n", response.GetProperty("subjects").EnumerateArray().Select(item => $"assunto assuntos {item.GetProperty("name").GetString()}")), metadata);

        yield return Chunk(caseId, responseId, responseType, fixturePath, "steps",
            string.Join("\n\n", response.GetProperty("steps").EnumerateArray().Select(step =>
                string.Join("\n", new[]
                {
                    $"ultima movimentacao step_id {step.GetProperty("step_id").GetRawText()}",
                    $"data {step.GetProperty("step_date").GetString()}",
                    $"conteudo {step.GetProperty("content").GetString()}",
                    $"fonte {step.GetProperty("source_name").GetString()}"
                }.Where(value => !string.IsNullOrWhiteSpace(value))))), metadata);

        yield return Chunk(caseId, responseId, responseType, fixturePath, "attachments",
            string.Join("\n", response.GetProperty("attachments").EnumerateArray().Select(item =>
                string.Join(" ", new[]
                {
                    $"quantidade anexos {response.GetProperty("attachments").GetArrayLength()}",
                    $"attachment_id {item.GetProperty("attachment_id").GetRawText()}",
                    $"anexo {item.GetProperty("attachment_name").GetString()}",
                    $"extensao {item.GetProperty("extension").GetString()}",
                    $"status {item.GetProperty("status").GetString()}"
                }.Where(value => !string.IsNullOrWhiteSpace(value))))), metadata);
    }

    private static LocalRagChunk BuildSummaryChunk(JsonElement page, string fixturePath, string responseId, string responseType)
    {
        var response = page.GetProperty("response_data");
        var data = response.TryGetProperty("data", out var array) && array.ValueKind == JsonValueKind.Array
            ? string.Join("\n\n", array.EnumerateArray().Select(item => item.GetString()).Where(value => !string.IsNullOrWhiteSpace(value)))
            : response.TryGetProperty("data", out var scalar)
                ? scalar.GetString() ?? string.Empty
                : string.Empty;
        var caseId = ExtractCaseId(data);
        return Chunk(
            caseId,
            responseId,
            responseType,
            fixturePath,
            "summary",
            data,
            BuildMetadata(page, response));
    }

    private static string ExtractCaseId(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var match = System.Text.RegularExpressions.Regex.Match(text, @"\b\d{7}-\d{2}\.\d{4}\.\d{1}\.\d{2}\.\d{4}\b");
        return match.Success ? match.Value : string.Empty;
    }

    private static LocalRagChunk Chunk(
        string caseId,
        string responseId,
        string responseType,
        string sourcePath,
        string section,
        string content,
        IReadOnlyDictionary<string, string> metadata)
    {
        var normalized = Normalize(content);
        var chunkId = $"{responseId}:{section}";
        return new LocalRagChunk(chunkId, caseId, responseId, responseType, sourcePath, section, normalized, metadata);
    }

    private static Dictionary<string, string> BuildMetadata(JsonElement page, JsonElement response)
    {
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["response_id"] = page.GetProperty("response_id").GetRawText().Trim('"'),
            ["request_id"] = page.GetProperty("request_id").GetRawText().Trim('"'),
            ["response_type"] = page.GetProperty("response_type").GetString() ?? string.Empty,
            ["source_name"] = response.TryGetProperty("crawler", out var crawler) && crawler.TryGetProperty("source_name", out var sourceName) ? sourceName.GetString() ?? string.Empty : string.Empty,
            ["cnj"] = response.TryGetProperty("code", out var code) ? code.GetString() ?? string.Empty : string.Empty,
            ["state"] = response.TryGetProperty("state", out var state) ? state.GetString() ?? string.Empty : string.Empty,
            ["city"] = response.TryGetProperty("city", out var city) ? city.GetString() ?? string.Empty : string.Empty,
            ["instance"] = response.TryGetProperty("instance", out var instance) ? instance.GetRawText().Trim('"') : string.Empty
        };

        return metadata.Where(pair => !string.IsNullOrWhiteSpace(pair.Value)).ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
    }

    private static string Normalize(string value) =>
        string.Join(' ', (value ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
}
