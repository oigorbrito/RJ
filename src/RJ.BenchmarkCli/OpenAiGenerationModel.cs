using System.Globalization;
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
    public const string HttpStage = "HTTP";
    public const string ResponseParseStage = "RESPONSE_PARSE";
    public const string StructuredOutputExtractionStage = "STRUCTURED_OUTPUT_EXTRACTION";
    public const string JsonDeserializationStage = "JSON_DESERIALIZATION";
    public const string LocalValidationStage = "LOCAL_VALIDATION";
    public const string ResultConstructionStage = "RESULT_CONSTRUCTION";
    public const string OtherStage = "OTHER";

    private static readonly Uri Endpoint = new("https://api.openai.com/v1/responses");
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

        return new OpenAiGenerationModel(httpClient ?? CreateDefaultClient(apiKey.Trim()), modelId.Trim());
    }

    public async Task<GenerationModelOutput> GenerateAsync(GenerationContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        if (context.Items.Count == 0)
        {
            throw Fail(LocalValidationStage, "OpenAI generation requires cited context evidence.");
        }

        var apiKey = Environment.GetEnvironmentVariable(ApiKeyEnvironmentVariable)?.Trim()
            ?? throw new InvalidOperationException($"{ApiKeyEnvironmentVariable} is required for the OpenAI generation adapter.");
        using var request = CreateRequest(context, apiKey);
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(_timeout);

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeoutCts.Token);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw Fail(HttpStage, "OpenAI request timed out.", exception);
        }
        catch (HttpRequestException exception)
        {
            throw Fail(HttpStage, "OpenAI request failed before a provider response was available.", exception);
        }

        using (response)
        {
            string responseText;
            try
            {
                responseText = await response.Content.ReadAsStringAsync(timeoutCts.Token);
            }
            catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
            {
                throw Fail(HttpStage, "OpenAI response read timed out.", exception, (int)response.StatusCode);
            }

            if (!response.IsSuccessStatusCode)
            {
                var provider = ReadProviderError(responseText);
                throw Fail(HttpStage, $"OpenAI request failed with status code {(int)response.StatusCode}.", null,
                    (int)response.StatusCode, provider.Code, provider.Message);
            }

            return ParseResponse(responseText, context);
        }
    }

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
                    name = "rj_generation_response",
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
                                anyOf = new object[] { new { type = "string" }, new { type = "null" } }
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
                new { role = "system", content = "Answer only with structured JSON matching the schema. Use only the supplied GenerationContext evidence." },
                new { role = "user", content = BuildUserPrompt(context) }
            }
        };

        var request = new HttpRequestMessage(HttpMethod.Post, Endpoint)
        {
            Content = new StringContent(JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return request;
    }

    private static string BuildUserPrompt(GenerationContext context)
    {
        var builder = new StringBuilder().Append("Question: ").Append(context.Query).Append('\n').Append("Evidence:\n");
        foreach (var item in context.Items)
        {
            builder.Append("- id=").Append(item.DocumentId)
                .Append("; sha256=").Append(item.ContentSha256)
                .Append("; startOffset=").Append(item.Position.StartOffset)
                .Append("; length=").Append(item.Position.Length)
                .Append("; text=").Append(item.Excerpt).Append('\n');
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
        catch (JsonException exception)
        {
            throw Fail(ResponseParseStage, "OpenAI response was not valid JSON.", exception);
        }

        using (document)
        {
            var metadata = Inspect(document.RootElement);
            string structuredText;
            try
            {
                structuredText = ExtractOutputText(document.RootElement);
            }
            catch (Exception exception) when (exception is InvalidOperationException or KeyNotFoundException)
            {
                throw Fail(StructuredOutputExtractionStage, "OpenAI response did not contain structured text output.", exception,
                    metadata: metadata);
            }

            ParsedOutput parsed;
            try
            {
                using var structured = JsonDocument.Parse(structuredText);
                parsed = ReadStructuredOutput(structured.RootElement);
            }
            catch (Exception exception) when (exception is JsonException or InvalidOperationException or KeyNotFoundException or FormatException or OverflowException)
            {
                throw Fail(JsonDeserializationStage, "OpenAI structured output did not match the expected JSON contract.", exception,
                    metadata: metadata with { HasStructuredJson = false });
            }

            metadata = metadata with { HasStructuredJson = true };
            foreach (var claim in parsed.Claims)
            {
                foreach (var citation in claim.Citations)
                {
                    if (!context.Items.Any(item => StringComparer.Ordinal.Equals(item.DocumentId, citation.DocumentId)
                        && StringComparer.Ordinal.Equals(item.ContentSha256, citation.ContentSha256)
                        && item.Position.StartOffset == citation.StartOffset && item.Position.Length == citation.Length))
                    {
                        throw Fail(LocalValidationStage, "OpenAI response cited evidence outside the supplied generation context.", metadata: metadata);
                    }
                }
            }

            try
            {
                return new GenerationModelOutput(parsed.Abstained, parsed.AbstentionReason,
                    parsed.Claims.Select(claim => new GenerationClaim(claim.Text,
                        claim.Citations.Select(c => new GenerationCitation(c.DocumentId, c.ContentSha256, c.StartOffset, c.Length)).ToArray())).ToArray());
            }
            catch (Exception exception)
            {
                throw Fail(ResultConstructionStage, "OpenAI structured output could not be converted to a generation result.", exception,
                    metadata: metadata);
            }
        }
    }

    private static string ExtractOutputText(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException("Response root is not an object.");
        }

        if (root.TryGetProperty("output_text", out var helper) && helper.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(helper.GetString()))
        {
            return helper.GetString()!;
        }

        if (!root.TryGetProperty("output", out var output) || output.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException("Response output is missing.");
        }

        var builder = new StringBuilder();
        foreach (var item in output.EnumerateArray())
        {
            if (!item.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array) continue;
            foreach (var part in content.EnumerateArray())
            {
                if (part.TryGetProperty("type", out var type) && type.ValueKind == JsonValueKind.String
                    && StringComparer.Ordinal.Equals(type.GetString(), "output_text")
                    && part.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String)
                {
                    builder.Append(text.GetString());
                }
            }
        }

        if (builder.Length == 0) throw new InvalidOperationException("No output_text content item found.");
        return builder.ToString();
    }

    private static ParsedOutput ReadStructuredOutput(JsonElement root)
    {
        var claims = new List<ParsedClaim>();
        foreach (var claim in root.GetProperty("claims").EnumerateArray())
        {
            var citations = new List<ParsedCitation>();
            foreach (var citation in claim.GetProperty("citations").EnumerateArray())
            {
                citations.Add(new ParsedCitation(
                    citation.GetProperty("documentId").GetString() ?? string.Empty,
                    citation.GetProperty("contentSha256").GetString() ?? string.Empty,
                    citation.GetProperty("startOffset").GetInt32(), citation.GetProperty("length").GetInt32()));
            }
            claims.Add(new ParsedClaim(claim.GetProperty("text").GetString() ?? string.Empty, citations));
        }

        var reason = root.TryGetProperty("abstention_reason", out var abstentionReason) && abstentionReason.ValueKind != JsonValueKind.Null
            ? abstentionReason.GetString() : null;
        return new ParsedOutput(root.GetProperty("abstained").GetBoolean(), reason, claims);
    }

    private static ResponseMetadata Inspect(JsonElement root)
    {
        string? objectType = root.ValueKind == JsonValueKind.Object && root.TryGetProperty("object", out var objectElement)
            && objectElement.ValueKind == JsonValueKind.String ? objectElement.GetString() : null;
        var count = root.ValueKind == JsonValueKind.Object && root.TryGetProperty("output", out var output) && output.ValueKind == JsonValueKind.Array
            ? output.GetArrayLength() : 0;
        var types = new HashSet<string>(StringComparer.Ordinal);
        var hasOutputText = root.ValueKind == JsonValueKind.Object && root.TryGetProperty("output_text", out var helper)
            && helper.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(helper.GetString());
        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("output", out output) && output.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in output.EnumerateArray())
            {
                if (!item.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array) continue;
                foreach (var part in content.EnumerateArray())
                {
                    if (part.TryGetProperty("type", out var type) && type.ValueKind == JsonValueKind.String && type.GetString() is { } value)
                    {
                        types.Add(value);
                        if (StringComparer.Ordinal.Equals(value, "output_text")) hasOutputText = true;
                    }
                }
            }
        }
        return new ResponseMetadata(objectType, count, types.OrderBy(x => x, StringComparer.Ordinal).ToArray(), hasOutputText, false);
    }

    private static ProviderError ReadProviderError(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            if (!document.RootElement.TryGetProperty("error", out var error) || error.ValueKind != JsonValueKind.Object) return default;
            return new ProviderError(
                error.TryGetProperty("code", out var code) && code.ValueKind == JsonValueKind.String ? code.GetString() : null,
                error.TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.String ? message.GetString() : null);
        }
        catch (JsonException) { return default; }
    }

    private OpenAiAdapterException Fail(string stage, string message, Exception? inner = null, int? httpStatus = null,
        string? providerErrorCode = null, string? providerErrorMessage = null, ResponseMetadata? metadata = null)
    {
        var exception = new OpenAiAdapterException(stage, message, inner, httpStatus, providerErrorCode, providerErrorMessage,
            metadata?.ObjectType, metadata?.OutputCount, metadata?.ContentTypes, metadata?.HasOutputText, metadata?.HasStructuredJson);
        WriteDiagnostic(exception);
        return exception;
    }

    private void WriteDiagnostic(OpenAiAdapterException exception)
    {
        if (!StringComparer.Ordinal.Equals(Environment.GetEnvironmentVariable(DiagnosticsEnvironmentVariable), "1")) return;
        var contentTypes = exception.ContentItemTypes.Count == 0 ? null : string.Join(',', exception.ContentItemTypes);
        Console.Error.WriteLine("OPENAI_DIAGNOSTIC "
            + $"stage={D(exception.Stage)} innerExceptionType={D(exception.InnerException?.GetType().FullName)} httpStatus={D(exception.HttpStatus?.ToString(CultureInfo.InvariantCulture))} "
            + $"providerErrorCode={D(exception.ProviderErrorCode)} providerErrorMessage={D(exception.ProviderErrorMessage)} exceptionMessage={D(exception.Message)} "
            + $"modelSent={D(_modelId)} endpoint={D(Endpoint.AbsoluteUri)} failureClass=OPENAI_ADAPTER "
            + $"responseObjectType={D(exception.ResponseObjectType)} outputItemCount={D(exception.OutputItemCount?.ToString(CultureInfo.InvariantCulture))} contentItemTypes={D(contentTypes)} "
            + $"hasOutputText={D(exception.HasOutputText?.ToString())} hasStructuredJson={D(exception.HasStructuredJson?.ToString())}");
    }

    private static string D(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "-";
        var safe = value.Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ').Trim();
        return safe.Length <= 512 ? safe : safe[..512];
    }

    private static HttpClient CreateDefaultClient(string apiKey)
    {
        var client = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return client;
    }

    private readonly record struct ProviderError(string? Code, string? Message);
    private sealed record ParsedCitation(string DocumentId, string ContentSha256, int StartOffset, int Length);
    private sealed record ParsedClaim(string Text, IReadOnlyList<ParsedCitation> Citations);
    private sealed record ParsedOutput(bool Abstained, string? AbstentionReason, IReadOnlyList<ParsedClaim> Claims);
    private sealed record ResponseMetadata(string? ObjectType, int OutputCount, IReadOnlyList<string> ContentTypes, bool HasOutputText, bool HasStructuredJson);

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
}
