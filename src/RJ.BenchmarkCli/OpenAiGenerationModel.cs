using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using RJ.Application.Benchmarking;
using RJ.Application.Generation;

namespace RJ.BenchmarkCli;

public sealed class OpenAiGenerationModel : IGenerationModel
{
    public const string ProviderName = "openai";
    public const string ModelId = "openai-oab-rulingbr-v1";
    public const string ProviderEnvironmentVariable = "RJ_GENERATION_PROVIDER";
    public const string ModelEnvironmentVariable = "RJ_GENERATION_MODEL";
    public const string ApiKeyEnvironmentVariable = "OPENAI_API_KEY";
    public const string DiagnosticsEnvironmentVariable = "RJ_BENCHMARK_DIAGNOSTICS";

    private const string ResponseFormatName = "rj_generation_response";
    private static readonly Uri DefaultBaseUri = new("https://api.openai.com/v1/");
    private readonly HttpClient _httpClient;
    private readonly string _modelId;
    private readonly TimeSpan _timeout;

    public OpenAiGenerationModel(HttpClient httpClient, string modelId, TimeSpan? timeout = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        if (string.IsNullOrWhiteSpace(modelId))
        {
            throw new ArgumentException("Model id is required for the OpenAI generation adapter.", nameof(modelId));
        }

        _modelId = modelId.Trim();
        _timeout = timeout ?? TimeSpan.FromSeconds(60);
    }

    public static bool IsProviderEnabled() =>
        StringComparer.Ordinal.Equals(Environment.GetEnvironmentVariable(ProviderEnvironmentVariable), ProviderName);

    public static OpenAiGenerationModel FromEnvironment(HttpClient? httpClient = null)
    {
        if (!IsProviderEnabled())
        {
            throw new ArgumentException($"Provider '{ProviderName}' is not enabled via {ProviderEnvironmentVariable}.", nameof(httpClient));
        }

        var modelId = Environment.GetEnvironmentVariable(ModelEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(modelId))
        {
            throw new ArgumentException($"Model id is required via {ModelEnvironmentVariable}.", nameof(httpClient));
        }

        var apiKey = Environment.GetEnvironmentVariable(ApiKeyEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException($"{ApiKeyEnvironmentVariable} is required for the OpenAI generation adapter.");
        }

        var client = httpClient ?? CreateDefaultClient(apiKey.Trim());
        return new OpenAiGenerationModel(client, modelId.Trim());
    }

    public async Task<GenerationModelOutput> GenerateAsync(
        GenerationContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        if (context.Items.Count == 0)
        {
            throw new InvalidOperationException("OpenAI generation requires cited context evidence.");
        }

        var apiKey = GetApiKey();
        using var request = CreateRequest(context, apiKey);
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(_timeout);

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeoutCts.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw Fail("HTTP", new TimeoutException("OpenAI request timed out."));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw Fail("HTTP", exception);
        }

        using (response)
        {
            string responseText;
            try
            {
                responseText = await response.Content.ReadAsStringAsync(timeoutCts.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw Fail("HTTP", new TimeoutException("OpenAI response read timed out."), response.StatusCode);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                throw Fail("HTTP", exception, response.StatusCode);
            }

            if (!response.IsSuccessStatusCode)
            {
                var providerError = ParseProviderError(responseText);
                throw Fail(
                    "HTTP",
                    new InvalidOperationException($"OpenAI request failed with status code {(int)response.StatusCode}."),
                    response.StatusCode,
                    providerError.Code,
                    providerError.Message);
            }

            return ParseResponse(responseText, context);
        }
    }

    private static string GetApiKey() =>
        Environment.GetEnvironmentVariable(ApiKeyEnvironmentVariable)?.Trim()
        ?? throw new InvalidOperationException($"{ApiKeyEnvironmentVariable} is required for the OpenAI generation adapter.");

    private HttpRequestMessage CreateRequest(GenerationContext context, string apiKey)
    {
        var payload = new
        {
            model = _modelId,
            temperature = 0,
            text = new
            {
                format = new
                {
                    type = "json_schema",
                    name = ResponseFormatName,
                    strict = true,
                    schema = new
                    {
                        type = "object",
                        additionalProperties = false,
                        required = new[] { "abstained", "abstention_reason", "claims" },
                        properties = new
                        {
                            abstained = new { type = "boolean" },
                            abstention_reason = new
                            {
                                anyOf = new object[]
                                {
                                    new { type = "string" },
                                    new { type = "null" }
                                }
                            },
                            claims = new
                            {
                                type = "array",
                                items = new
                                {
                                    type = "object",
                                    additionalProperties = false,
                                    required = new[] { "text", "citations" },
                                    properties = new
                                    {
                                        text = new { type = "string" },
                                        citations = new
                                        {
                                            type = "array",
                                            items = new
                                            {
                                                type = "object",
                                                additionalProperties = false,
                                                required = new[] { "documentId", "contentSha256", "startOffset", "length" },
                                                properties = new
                                                {
                                                    documentId = new { type = "string" },
                                                    contentSha256 = new { type = "string" },
                                                    startOffset = new { type = "integer" },
                                                    length = new { type = "integer" }
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            },
            input = new object[]
            {
                new
                {
                    role = "system",
                    content = "You must answer only with structured JSON matching the schema. Use only evidence ids present in the provided GenerationContext. Do not use guidelines, model_answer, expected claims, oracle, evaluation results, or any external evidence."
                },
                new
                {
                    role = "user",
                    content = BuildUserPrompt(context)
                }
            }
        };

        var request = new HttpRequestMessage(HttpMethod.Post, new Uri(DefaultBaseUri, "responses"))
        {
            Content = new StringContent(JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return request;
    }

    private static string BuildUserPrompt(GenerationContext context)
    {
        var builder = new StringBuilder();
        builder.Append("Question: ").Append(context.Query).Append('\n');
        builder.Append("Evidence:").Append('\n');
        foreach (var item in context.Items)
        {
            builder.Append("- id=")
                .Append(item.DocumentId)
                .Append("; sha256=")
                .Append(item.ContentSha256)
                .Append("; startOffset=")
                .Append(item.Position.StartOffset)
                .Append("; length=")
                .Append(item.Position.Length)
                .Append("; text=")
                .Append(item.Excerpt)
                .Append('\n');
        }

        return builder.ToString();
    }

    private GenerationModelOutput ParseResponse(string responseText, GenerationContext context)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(responseText);
        }
        catch (Exception exception)
        {
            throw Fail("RESPONSE_PARSE", exception);
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw Fail("RESPONSE_PARSE", new InvalidOperationException("OpenAI response was not a JSON object."));
            }

            string structuredText;
            ResponseStructure structure;
            try
            {
                (structuredText, structure) = ExtractStructuredText(root);
            }
            catch (Exception exception) when (exception is not OpenAiAdapterException)
            {
                throw Fail("STRUCTURED_OUTPUT_EXTRACTION", exception, structure: DescribeStructure(root));
            }

            JsonDocument modelJson;
            try
            {
                modelJson = JsonDocument.Parse(structuredText);
            }
            catch (Exception exception)
            {
                throw Fail("JSON_DESERIALIZATION", exception, structure: structure);
            }

            using (modelJson)
            {
                var modelRoot = modelJson.RootElement;
                bool abstained;
                string? abstentionReason;
                List<(string Text, List<GenerationCitation> Citations)> claimData;

                try
                {
                    abstained = modelRoot.GetProperty("abstained").GetBoolean();
                    abstentionReason = modelRoot.TryGetProperty("abstention_reason", out var abstentionReasonElement)
                        && abstentionReasonElement.ValueKind != JsonValueKind.Null
                        ? abstentionReasonElement.GetString()
                        : null;

                    claimData = new List<(string Text, List<GenerationCitation> Citations)>();
                    foreach (var claimElement in modelRoot.GetProperty("claims").EnumerateArray())
                    {
                        var text = claimElement.GetProperty("text").GetString() ?? string.Empty;
                        var citations = new List<GenerationCitation>();
                        foreach (var citationElement in claimElement.GetProperty("citations").EnumerateArray())
                        {
                            var citation = new GenerationCitation(
                                citationElement.GetProperty("documentId").GetString() ?? string.Empty,
                                citationElement.GetProperty("contentSha256").GetString() ?? string.Empty,
                                citationElement.GetProperty("startOffset").GetInt32(),
                                citationElement.GetProperty("length").GetInt32());

                            if (!context.Items.Any(item =>
                                    StringComparer.Ordinal.Equals(item.DocumentId, citation.DocumentId)
                                    && StringComparer.Ordinal.Equals(item.ContentSha256, citation.ContentSha256)
                                    && item.Position.StartOffset == citation.StartOffset
                                    && item.Position.Length == citation.Length))
                            {
                                throw new InvalidOperationException("OpenAI response cited evidence outside the supplied generation context.");
                            }

                            citations.Add(citation);
                        }

                        claimData.Add((text, citations));
                    }
                }
                catch (Exception exception)
                {
                    throw Fail("LOCAL_VALIDATION", exception, structure: structure);
                }

                try
                {
                    var claims = claimData
                        .Select(item => new GenerationClaim(item.Text, item.Citations))
                        .ToArray();
                    return new GenerationModelOutput(abstained, abstentionReason, claims);
                }
                catch (Exception exception)
                {
                    throw Fail("RESULT_CONSTRUCTION", exception, structure: structure);
                }
            }
        }
    }

    private static (string Text, ResponseStructure Structure) ExtractStructuredText(JsonElement root)
    {
        var structure = DescribeStructure(root);
        if (!root.TryGetProperty("output", out var output) || output.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException("OpenAI response did not contain an output array.");
        }

        foreach (var outputItem in output.EnumerateArray())
        {
            if (!outputItem.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var contentItem in content.EnumerateArray())
            {
                if (!contentItem.TryGetProperty("type", out var type)
                    || !StringComparer.Ordinal.Equals(type.GetString(), "output_text")
                    || !contentItem.TryGetProperty("text", out var text))
                {
                    continue;
                }

                var value = text.GetString();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return (value, structure);
                }
            }
        }

        throw new InvalidOperationException("OpenAI response did not contain structured output_text content.");
    }

    private static ResponseStructure DescribeStructure(JsonElement root)
    {
        if (!root.TryGetProperty("output", out var output) || output.ValueKind != JsonValueKind.Array)
        {
            return new ResponseStructure(root.ValueKind.ToString(), 0, string.Empty, false);
        }

        var contentTypes = new List<string>();
        var hasOutputText = false;
        foreach (var outputItem in output.EnumerateArray())
        {
            if (!outputItem.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var contentItem in content.EnumerateArray())
            {
                if (!contentItem.TryGetProperty("type", out var type))
                {
                    continue;
                }

                var value = type.GetString();
                if (string.IsNullOrWhiteSpace(value))
                {
                    continue;
                }

                contentTypes.Add(value);
                hasOutputText |= StringComparer.Ordinal.Equals(value, "output_text");
            }
        }

        return new ResponseStructure(
            root.ValueKind.ToString(),
            output.GetArrayLength(),
            string.Join(',', contentTypes.Distinct(StringComparer.Ordinal)),
            hasOutputText);
    }

    private OpenAiAdapterException Fail(
        string stage,
        Exception exception,
        HttpStatusCode? statusCode = null,
        string? providerErrorCode = null,
        string? providerErrorMessage = null,
        ResponseStructure? structure = null)
    {
        WriteDiagnostic(stage, exception, statusCode, providerErrorCode, providerErrorMessage, structure);
        return new OpenAiAdapterException("OpenAI generation adapter failed.", exception);
    }

    private void WriteDiagnostic(
        string stage,
        Exception exception,
        HttpStatusCode? statusCode,
        string? providerErrorCode,
        string? providerErrorMessage,
        ResponseStructure? structure)
    {
        if (!StringComparer.Ordinal.Equals(Environment.GetEnvironmentVariable(DiagnosticsEnvironmentVariable), "1"))
        {
            return;
        }

        var endpoint = new Uri(DefaultBaseUri, "responses");
        var fields = new List<string>
        {
            "OPENAI_DIAGNOSTIC",
            $"stage={Sanitize(stage)}",
            $"innerExceptionType={Sanitize(exception.GetType().FullName)}",
            $"httpStatus={(statusCode is null ? string.Empty : ((int)statusCode.Value).ToString())}",
            $"providerErrorCode={Sanitize(providerErrorCode)}",
            $"providerErrorMessage={Sanitize(providerErrorMessage)}",
            $"exceptionMessage={Sanitize(exception.Message)}",
            $"modelSent={Sanitize(_modelId)}",
            $"endpoint={Sanitize(endpoint.ToString())}",
            $"failureClass={Sanitize(exception.GetType().Name)}"
        };

        if (structure is not null)
        {
            fields.Add($"responseObjectType={Sanitize(structure.Value.ObjectType)}");
            fields.Add($"outputItemCount={structure.Value.OutputItemCount}");
            fields.Add($"contentItemTypes={Sanitize(structure.Value.ContentItemTypes)}");
            fields.Add($"hasOutputText={structure.Value.HasOutputText}");
        }

        Console.Error.WriteLine(string.Join(' ', fields));
    }

    private static string Sanitize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return value
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Replace('\t', ' ')
            .Trim();
    }

    private static (string? Code, string? Message) ParseProviderError(string responseText)
    {
        try
        {
            using var document = JsonDocument.Parse(responseText);
            if (!document.RootElement.TryGetProperty("error", out var error) || error.ValueKind != JsonValueKind.Object)
            {
                return (null, null);
            }

            var code = error.TryGetProperty("code", out var codeElement) ? codeElement.GetString() : null;
            var message = error.TryGetProperty("message", out var messageElement) ? messageElement.GetString() : null;
            return (code, message);
        }
        catch (JsonException)
        {
            return (null, null);
        }
    }

    private static HttpClient CreateDefaultClient(string apiKey)
    {
        var client = new HttpClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        client.Timeout = Timeout.InfiniteTimeSpan;
        return client;
    }

    private readonly record struct ResponseStructure(
        string ObjectType,
        int OutputItemCount,
        string ContentItemTypes,
        bool HasOutputText);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
}
