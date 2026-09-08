using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using RJ.Application.Generation;

namespace RJ.BenchmarkCli;

public sealed class OpenAiGenerationModel : IGenerationModel
{
    public const string ProviderName = "openai";
    public const string ModelId = "openai-oab-rulingbr-v1";
    public const string ProviderEnvironmentVariable = "RJ_GENERATION_PROVIDER";
    public const string ModelEnvironmentVariable = "RJ_GENERATION_MODEL";
    public const string ApiKeyEnvironmentVariable = "OPENAI_API_KEY";

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

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeoutCts.Token);
        var responseText = await response.Content.ReadAsStringAsync(timeoutCts.Token);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"OpenAI request failed with status code {(int)response.StatusCode}.");
        }

        return ParseResponse(responseText, context);
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
            seed = 0,
            text = new
            {
                format = new
                {
                    type = "json_schema",
                    json_schema = new
                    {
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
                                abstention_reason = new[] { "string", "null" },
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

    private static GenerationModelOutput ParseResponse(string responseText, GenerationContext context)
    {
        using var document = ParseJson(responseText, "OpenAI response was not valid JSON.");
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException("OpenAI response was not a JSON object.");
        }

        var output = root.TryGetProperty("output_text", out var outputTextElement)
            ? outputTextElement.GetString()
            : root.GetProperty("text").GetString();

        if (string.IsNullOrWhiteSpace(output))
        {
            throw new InvalidOperationException("OpenAI response did not contain structured text output.");
        }

        using var modelJson = ParseJson(output, "OpenAI structured output was not valid JSON.");
        var modelRoot = modelJson.RootElement;
        var abstained = modelRoot.GetProperty("abstained").GetBoolean();
        var abstentionReason = modelRoot.TryGetProperty("abstention_reason", out var abstentionReasonElement) && abstentionReasonElement.ValueKind != JsonValueKind.Null
            ? abstentionReasonElement.GetString()
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
                    throw new InvalidOperationException("OpenAI response cited evidence outside the supplied generation context.");
                }

                citations.Add(citation);
            }

            claims.Add(new GenerationClaim(text, citations));
        }

        return new GenerationModelOutput(abstained, abstentionReason, claims);
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

    private static HttpClient CreateDefaultClient(string apiKey)
    {
        var client = new HttpClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        client.Timeout = Timeout.InfiniteTimeSpan;
        return client;
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
}
