using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using RJ.Application.Generation;

namespace RJ.Infrastructure.Generation;

public sealed class OpenAiGenerationModel : IGenerationModel
{
    public const string ProviderName = "openai";
    public const string ProviderEnvironmentVariable = "RJ_GENERATION_PROVIDER";
    public const string ModelEnvironmentVariable = "RJ_GENERATION_MODEL";
    public const string ApiKeyEnvironmentVariable = "OPENAI_API_KEY";

    private static readonly Uri DefaultBaseUri = new("https://api.openai.com/v1/");
    private readonly HttpClient httpClient;
    private readonly string modelId;
    private readonly TimeSpan timeout;

    public OpenAiGenerationModel(HttpClient httpClient, string modelId, TimeSpan? timeout = null)
    {
        this.httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        if (string.IsNullOrWhiteSpace(modelId))
        {
            throw new ArgumentException("Model id is required for the OpenAI generation adapter.", nameof(modelId));
        }

        this.modelId = modelId.Trim();
        this.timeout = timeout ?? TimeSpan.FromSeconds(60);
    }

    public static OpenAiGenerationModel FromEnvironment(HttpClient? httpClient = null)
    {
        var model = Environment.GetEnvironmentVariable(ModelEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(model))
        {
            throw new InvalidOperationException($"{ModelEnvironmentVariable} is required when RJ_GENERATION_PROVIDER=openai.");
        }

        var apiKey = Environment.GetEnvironmentVariable(ApiKeyEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException($"{ApiKeyEnvironmentVariable} is required when RJ_GENERATION_PROVIDER=openai.");
        }

        return new OpenAiGenerationModel(httpClient ?? CreateDefaultClient(), model);
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

        var apiKey = Environment.GetEnvironmentVariable(ApiKeyEnvironmentVariable)?.Trim();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException($"{ApiKeyEnvironmentVariable} is required for OpenAI generation.");
        }

        using var request = CreateRequest(context, apiKey);
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);

        using var response = await httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            timeoutCts.Token);
        var responseText = await response.Content.ReadAsStringAsync(timeoutCts.Token);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"OpenAI request failed with status code {(int)response.StatusCode}.");
        }

        return ParseResponse(responseText, context);
    }

    private HttpRequestMessage CreateRequest(GenerationContext context, string apiKey)
    {
        var payload = new
        {
            model = modelId,
            temperature = 0,
            seed = 0,
            text = new
            {
                format = new
                {
                    type = "json_schema",
                    name = "rj_generation_output",
                    strict = true,
                    schema = new
                    {
                        type = "object",
                        additionalProperties = false,
                        required = new[] { "abstained", "abstention_reason", "claims" },
                        properties = new
                        {
                            abstained = new { type = "boolean" },
                            abstention_reason = new { type = new[] { "string", "null" } },
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
                    content = "Answer only with structured JSON matching the schema. Use only evidence supplied in GenerationContext. Every factual claim must cite supplied evidence. If the evidence is insufficient, abstain."
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
            Content = new StringContent(
                JsonSerializer.Serialize(payload, JsonOptions),
                Encoding.UTF8,
                "application/json")
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

    private static GenerationModelOutput ParseResponse(string responseText, GenerationContext context)
    {
        using var responseJson = ParseJson(responseText, "OpenAI response was not valid JSON.");
        var root = responseJson.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException("OpenAI response was not a JSON object.");
        }

        var output = ExtractOutputText(root);
        if (string.IsNullOrWhiteSpace(output))
        {
            throw new InvalidOperationException("OpenAI response did not contain structured text output.");
        }

        using var modelJson = ParseJson(output, "OpenAI structured output was not valid JSON.");
        var modelRoot = modelJson.RootElement;
        var abstained = modelRoot.GetProperty("abstained").GetBoolean();
        var abstentionReason = modelRoot.TryGetProperty("abstention_reason", out var reasonElement)
            && reasonElement.ValueKind != JsonValueKind.Null
            ? reasonElement.GetString()
            : null;

        var claims = new List<GenerationClaim>();
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
                    throw new InvalidOperationException(
                        "OpenAI response cited evidence outside the supplied generation context.");
                }

                citations.Add(citation);
            }

            claims.Add(new GenerationClaim(text, citations));
        }

        return new GenerationModelOutput(abstained, abstentionReason, claims);
    }

    private static string? ExtractOutputText(JsonElement root)
    {
        if (root.TryGetProperty("output_text", out var outputTextElement)
            && outputTextElement.ValueKind == JsonValueKind.String)
        {
            return outputTextElement.GetString();
        }

        if (!root.TryGetProperty("output", out var outputElement)
            || outputElement.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var outputItem in outputElement.EnumerateArray())
        {
            if (!outputItem.TryGetProperty("content", out var contentElement)
                || contentElement.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var contentItem in contentElement.EnumerateArray())
            {
                if (contentItem.TryGetProperty("text", out var textElement)
                    && textElement.ValueKind == JsonValueKind.String
                    && !string.IsNullOrWhiteSpace(textElement.GetString()))
                {
                    return textElement.GetString();
                }
            }
        }

        return null;
    }

    private static JsonDocument ParseJson(string json, string message)
    {
        try
        {
            return JsonDocument.Parse(json);
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException(message, exception);
        }
    }

    private static HttpClient CreateDefaultClient()
    {
        var client = new HttpClient
        {
            Timeout = Timeout.InfiniteTimeSpan
        };
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return client;
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
}
