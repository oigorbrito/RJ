using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using RJ.Application.Benchmarking;
using RJ.Application.Generation;
using RJ.Application.Retrieval;
using RJ.BenchmarkCli;

namespace RJ.DomainTests;

public sealed class OpenAiGenerationModelTests
{
    [Fact]
    public void FromEnvironment_fails_closed_when_provider_is_missing()
    {
        var state = CaptureEnvironment();
        try
        {
            Environment.SetEnvironmentVariable(OpenAiGenerationModel.ProviderEnvironmentVariable, null);
            Environment.SetEnvironmentVariable(OpenAiGenerationModel.ModelEnvironmentVariable, null);
            Environment.SetEnvironmentVariable(OpenAiGenerationModel.ApiKeyEnvironmentVariable, null);
            Assert.Throws<ArgumentException>(() => OpenAiGenerationModel.FromEnvironment());
        }
        finally
        {
            RestoreEnvironment(state);
        }
    }

    [Fact]
    public async Task GenerateAsync_sends_correct_payload_and_parses_responses_rest_shape()
    {
        var handler = new RecordingHandler(Ok(CreateValidResponse()));
        var model = new OpenAiGenerationModel(new HttpClient(handler), "gpt-5.6");
        var state = CaptureEnvironment();

        try
        {
            Environment.SetEnvironmentVariable(OpenAiGenerationModel.ApiKeyEnvironmentVariable, "secret-key");
            var output = await model.GenerateAsync(CreateContext(), CancellationToken.None);

            Assert.False(output.Abstained);
            Assert.Single(output.Claims);
            Assert.DoesNotContain("secret-key", handler.RequestBody, StringComparison.Ordinal);
            Assert.DoesNotContain("oracle", handler.RequestBody, StringComparison.OrdinalIgnoreCase);

            using var requestJson = JsonDocument.Parse(handler.RequestBody);
            var root = requestJson.RootElement;
            Assert.Equal("gpt-5.6", root.GetProperty("model").GetString());
            Assert.False(root.TryGetProperty("seed", out _));

            var format = root.GetProperty("text").GetProperty("format");
            Assert.Equal("json_schema", format.GetProperty("type").GetString());
            Assert.Equal("rj_generation_response", format.GetProperty("name").GetString());
            Assert.True(format.TryGetProperty("schema", out var schema));
            Assert.False(format.TryGetProperty("json_schema", out _));
            Assert.True(schema.GetProperty("properties").GetProperty("abstention_reason").TryGetProperty("anyOf", out _));
        }
        finally
        {
            RestoreEnvironment(state);
        }
    }

    [Fact]
    public async Task GenerateAsync_diagnoses_invalid_structured_json_after_http_200()
    {
        var handler = new RecordingHandler(Ok(CreateResponseWithText("not-json")));
        var diagnostic = await ExecuteFailureWithDiagnostics(handler);

        Assert.Contains("stage=JSON_DESERIALIZATION", diagnostic, StringComparison.Ordinal);
        Assert.Contains("outputItemCount=1", diagnostic, StringComparison.Ordinal);
        Assert.Contains("contentItemTypes=output_text", diagnostic, StringComparison.Ordinal);
        Assert.Contains("hasOutputText=True", diagnostic, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-key", diagnostic, StringComparison.Ordinal);
        Assert.DoesNotContain("not-json", diagnostic, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GenerateAsync_diagnoses_missing_output_text_after_http_200()
    {
        var response = """
        {
          "id": "resp-test",
          "object": "response",
          "status": "completed",
          "output": [{
            "type": "message",
            "role": "assistant",
            "status": "completed",
            "content": [{ "type": "refusal", "refusal": "cannot answer" }]
          }]
        }
        """;

        var diagnostic = await ExecuteFailureWithDiagnostics(new RecordingHandler(Ok(response)));
        Assert.Contains("stage=STRUCTURED_OUTPUT_EXTRACTION", diagnostic, StringComparison.Ordinal);
        Assert.Contains("contentItemTypes=refusal", diagnostic, StringComparison.Ordinal);
        Assert.Contains("hasOutputText=False", diagnostic, StringComparison.Ordinal);
        Assert.DoesNotContain("cannot answer", diagnostic, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GenerateAsync_rejects_citation_outside_context_as_local_validation_failure()
    {
        const string invalid = "{\"abstained\":false,\"abstention_reason\":null,\"claims\":[{\"text\":\"A tutela foi deferida.\",\"citations\":[{\"documentId\":\"doc-x\",\"contentSha256\":\"ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff\",\"startOffset\":0,\"length\":1}]}]}";
        var diagnostic = await ExecuteFailureWithDiagnostics(new RecordingHandler(Ok(CreateResponseWithText(invalid))));
        Assert.Contains("stage=LOCAL_VALIDATION", diagnostic, StringComparison.Ordinal);
    }

    private static async Task<string> ExecuteFailureWithDiagnostics(RecordingHandler handler)
    {
        var model = new OpenAiGenerationModel(new HttpClient(handler), "gpt-5.6");
        var state = CaptureEnvironment();
        var previousError = Console.Error;
        using var writer = new StringWriter();

        try
        {
            Environment.SetEnvironmentVariable(OpenAiGenerationModel.ApiKeyEnvironmentVariable, "secret-key");
            Environment.SetEnvironmentVariable(OpenAiGenerationModel.DiagnosticsEnvironmentVariable, "1");
            Console.SetError(writer);
            await Assert.ThrowsAsync<OpenAiAdapterException>(() => model.GenerateAsync(CreateContext(), CancellationToken.None));
            return writer.ToString();
        }
        finally
        {
            Console.SetError(previousError);
            RestoreEnvironment(state);
        }
    }

    private static HttpResponseMessage Ok(string content) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(content, Encoding.UTF8, "application/json")
    };

    private static string CreateValidResponse()
    {
        const string output = "{\"abstained\":false,\"abstention_reason\":null,\"claims\":[{\"text\":\"A tutela foi deferida.\",\"citations\":[{\"documentId\":\"doc-1\",\"contentSha256\":\"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\",\"startOffset\":0,\"length\":22}]}]}";
        return CreateResponseWithText(output);
    }

    private static string CreateResponseWithText(string text) => JsonSerializer.Serialize(new
    {
        id = "resp-test",
        @object = "response",
        status = "completed",
        output = new object[]
        {
            new
            {
                type = "message",
                role = "assistant",
                status = "completed",
                content = new object[] { new { type = "output_text", text, annotations = Array.Empty<object>() } }
            }
        }
    });

    private static GenerationContext CreateContext()
    {
        const string excerpt = "A tutela foi deferida.";
        var position = SourcePosition.Create(0, excerpt.Length, excerpt.Length);
        var item = new GenerationContextItem(
            "case-1", "doc-1", "source.txt", new string('a', 64), excerpt, position, 1f);
        return new GenerationContext("case-1", "Qual foi a decisão?", 1000, excerpt.Length, [item]);
    }

    private static EnvironmentState CaptureEnvironment() => new(
        Environment.GetEnvironmentVariable(OpenAiGenerationModel.ProviderEnvironmentVariable),
        Environment.GetEnvironmentVariable(OpenAiGenerationModel.ModelEnvironmentVariable),
        Environment.GetEnvironmentVariable(OpenAiGenerationModel.ApiKeyEnvironmentVariable),
        Environment.GetEnvironmentVariable(OpenAiGenerationModel.DiagnosticsEnvironmentVariable));

    private static void RestoreEnvironment(EnvironmentState state)
    {
        Environment.SetEnvironmentVariable(OpenAiGenerationModel.ProviderEnvironmentVariable, state.Provider);
        Environment.SetEnvironmentVariable(OpenAiGenerationModel.ModelEnvironmentVariable, state.Model);
        Environment.SetEnvironmentVariable(OpenAiGenerationModel.ApiKeyEnvironmentVariable, state.ApiKey);
        Environment.SetEnvironmentVariable(OpenAiGenerationModel.DiagnosticsEnvironmentVariable, state.Diagnostics);
    }

    private sealed record EnvironmentState(string? Provider, string? Model, string? ApiKey, string? Diagnostics);

    private sealed class RecordingHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        public string RequestBody { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestBody = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            return response;
        }
    }
}
