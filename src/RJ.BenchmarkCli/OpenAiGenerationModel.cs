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

    private static readonly Uri DefaultBaseUri = new("https://api.openai.com/v1/");
    private readonly HttpClient _httpClient;
    private readonly string _adapterModelId;
    private readonly string _providerModelId;
    private readonly TimeSpan _timeout;

    public OpenAiGenerationModel(HttpClient httpClient, string modelId, TimeSpan? timeout = null)
        : this(httpClient, modelId, modelId, timeout)
    {
    }

    private OpenAiGenerationModel(HttpClient httpClient, string adapterModelId, string providerModelId, TimeSpan? timeout = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        if (string.IsNullOrWhiteSpace(adapterModelId))
        {
            throw new ArgumentException("Model id is required for the OpenAI generation adapter.", nameof(adapterModelId));
        }

        if (string.IsNullOrWhiteSpace(providerModelId))
        {
            throw new ArgumentException("Provider model id is required for the OpenAI generation adapter.", nameof(providerModelId));
        }

        _adapterModelId = adapterModelId.Trim();
        _providerModelId = providerModelId.Trim();
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
        return new OpenAiGenerationModel(client, ModelId, modelId.Trim());
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

        try
        {
            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeoutCts.Token);
            var responseText = await response.Content.ReadAsStringAsync(timeoutCts.Token);
            if (!response.IsSuccessStatusCode)
            {
                throw CreateProviderException(
                    new InvalidOperationException($"OpenAI request failed with status code {(int)response.StatusCode}."),
                    statusCode: response.StatusCode,
                    responseText: responseText,
                    failureClass: OpenAiAdapterFailureClass.Other,
                    stage: OpenAiAdapterStage.Http);
            }

            return ParseResponse(responseText, context);
        }
        catch (OpenAiAdapterException)
        {
            throw;
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw CreateProviderException(exception, statusCode: null, responseText: null, failureClass: OpenAiAdapterFailureClass.Timeout, stage: OpenAiAdapterStage.Http);
        }
        catch (HttpRequestException exception)
        {
            throw CreateProviderException(exception, statusCode: null, responseText: null, failureClass: OpenAiAdapterFailureClass.Network, stage: OpenAiAdapterStage.Http);
        }
        catch (JsonException exception)
        {
            throw CreateProviderException(exception, statusCode: null, responseText: null, failureClass: OpenAiAdapterFailureClass.ResponseSchema, stage: OpenAiAdapterStage.JsonDeserialization);
        }
        catch (InvalidOperationException exception)
        {
            throw CreateProviderException(exception, statusCode: null, responseText: null, failureClass: null, stage: OpenAiAdapterStage.Other);
        }
    }

    private static string GetApiKey() =>
        Environment.GetEnvironmentVariable(ApiKeyEnvironmentVariable)?.Trim()
        ?? throw new InvalidOperationException($"{ApiKeyEnvironmentVariable} is required for the OpenAI generation adapter.");

    private HttpRequestMessage CreateRequest(GenerationContext context, string apiKey)
    {
        var payload = new
        {
            model = _providerModelId,
            temperature = 0,
            text = new
            {
                format = new
                {
                    type = "json_schema",
                    name = "rj_generation_response",
                    schema = new
                    {
                        strict = true,
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
        try
        {
            using var document = JsonDocument.Parse(responseText);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw CreateStructuredOutputException("OpenAI response was not a JSON object.", null, null, OpenAiAdapterStage.ResponseParse);
            }

            var output = ExtractStructuredOutput(root);
            if (string.IsNullOrWhiteSpace(output))
            {
                throw CreateStructuredOutputException("OpenAI response did not contain structured text output.", null, null, OpenAiAdapterStage.StructuredOutputExtraction);
            }

            return ParseStructuredOutput(output, context);
        }
        catch (JsonException exception)
        {
            throw CreateProviderException(exception, statusCode: null, responseText: null, failureClass: OpenAiAdapterFailureClass.ResponseSchema, stage: OpenAiAdapterStage.ResponseParse);
        }
    }

    private static string? ExtractStructuredOutput(JsonElement root)
    {
        if (root.TryGetProperty("output_text", out var outputTextElement))
        {
            return outputTextElement.GetString();
        }

        if (root.TryGetProperty("output", out var outputElement) && outputElement.ValueKind == JsonValueKind.Array)
        {
            var texts = new List<string>();
            foreach (var item in outputElement.EnumerateArray())
            {
                if (!item.TryGetProperty("content", out var contentElement) || contentElement.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                foreach (var contentItem in contentElement.EnumerateArray())
                {
                    if (contentItem.TryGetProperty("type", out var typeElement) && string.Equals(typeElement.GetString(), "output_text", StringComparison.Ordinal))
                    {
                        var text = contentItem.TryGetProperty("text", out var contentTextElement) ? contentTextElement.GetString() : null;
                        if (!string.IsNullOrWhiteSpace(text))
                        {
                            texts.Add(text);
                        }
                    }
                }
            }

            if (texts.Count > 0)
            {
                return string.Join(string.Empty, texts);
            }
        }

        if (root.TryGetProperty("text", out var textElement))
        {
            return textElement.GetString();
        }

        return null;
    }

    private GenerationModelOutput ParseStructuredOutput(string output, GenerationContext context)
    {
        try
        {
            using var modelJson = JsonDocument.Parse(output);
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
                        throw CreateStructuredOutputException(
                            "OpenAI response cited evidence outside the supplied generation context.",
                            null,
                            null,
                            OpenAiAdapterStage.LocalValidation);
                    }

                    citations.Add(citation);
                }

                claims.Add(new GenerationClaim(text, citations));
            }

            return new GenerationModelOutput(abstained, abstentionReason, claims);
        }
        catch (JsonException exception)
        {
            throw CreateProviderException(exception, statusCode: null, responseText: null, failureClass: OpenAiAdapterFailureClass.ResponseSchema, stage: OpenAiAdapterStage.JsonDeserialization);
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

    private OpenAiAdapterException CreateProviderException(
        Exception exception,
        HttpStatusCode? statusCode,
        string? responseText,
        OpenAiAdapterFailureClass? failureClass = null,
        OpenAiAdapterStage stage = OpenAiAdapterStage.Other)
    {
        var diagnostic = CreateDiagnostic(
            stage,
            exception.GetType().FullName ?? exception.GetType().Name,
            statusCode?.ToString(),
            TryReadProviderErrorCode(responseText),
            TryReadProviderErrorMessage(responseText) ?? SanitizeMessage(exception.Message),
            SanitizeMessage(exception.Message),
            failureClass ?? Classify(exception, statusCode, responseText));
        return new OpenAiAdapterException("OpenAI generation failed.", exception, diagnostic);
    }

    private OpenAiAdapterException CreateStructuredOutputException(string message, Exception? innerException, string? responseText, OpenAiAdapterStage stage)
    {
        var diagnostic = CreateDiagnostic(
            stage,
            innerException?.GetType().FullName ?? typeof(InvalidOperationException).FullName!,
            null,
            TryReadProviderErrorCode(responseText),
            TryReadProviderErrorMessage(responseText) ?? message,
            message,
            OpenAiAdapterFailureClass.ResponseSchema);
        return new OpenAiAdapterException(message, innerException, diagnostic);
    }

    private OpenAiAdapterDiagnostic CreateDiagnostic(
        OpenAiAdapterStage stage,
        string innerExceptionType,
        string? httpStatus,
        string? providerErrorCode,
        string providerErrorMessage,
        string exceptionMessage,
        OpenAiAdapterFailureClass failureClass) =>
        new(
            stage,
            innerExceptionType,
            httpStatus,
            providerErrorCode,
            providerErrorMessage,
            exceptionMessage,
            _providerModelId,
            new Uri(DefaultBaseUri, "responses").ToString(),
            failureClass);

    private static string? TryReadProviderErrorCode(string? responseText)
    {
        if (string.IsNullOrWhiteSpace(responseText))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(responseText);
            var root = document.RootElement;
            if (root.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object && error.TryGetProperty("code", out var code))
            {
                return code.GetString();
            }
        }
        catch
        {
        }

        return null;
    }

    private static string? TryReadProviderErrorMessage(string? responseText)
    {
        if (string.IsNullOrWhiteSpace(responseText))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(responseText);
            var root = document.RootElement;
            if (root.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object && error.TryGetProperty("message", out var message))
            {
                return SanitizeMessage(message.GetString());
            }
        }
        catch
        {
        }

        return null;
    }

    private static string SanitizeMessage(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return "n/a";
        }

        var sanitized = message.Trim();
        return sanitized.Length <= 240 ? sanitized : sanitized[..240];
    }

    private static OpenAiAdapterFailureClass Classify(Exception exception, HttpStatusCode? statusCode, string? responseText)
    {
        if (exception is TaskCanceledException)
        {
            return OpenAiAdapterFailureClass.Timeout;
        }

        if (exception is HttpRequestException)
        {
            return OpenAiAdapterFailureClass.Network;
        }

        if (statusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            return OpenAiAdapterFailureClass.Authentication;
        }

        if (statusCode == HttpStatusCode.NotFound)
        {
            return OpenAiAdapterFailureClass.ModelNotFound;
        }

        if (statusCode == HttpStatusCode.TooManyRequests)
        {
            return OpenAiAdapterFailureClass.RateLimit;
        }

        if (!string.IsNullOrWhiteSpace(responseText) && TryReadProviderErrorCode(responseText) is not null)
        {
            return OpenAiAdapterFailureClass.RequestSchema;
        }

        return OpenAiAdapterFailureClass.Other;
    }
}
