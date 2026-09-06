using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using RJ.Application.Benchmarking;
using RJ.Application.Generation;
using RJ.BenchmarkCli;

namespace RJ.DomainTests;

public sealed class OpenAiGenerationModelTests
{
    [Fact]
    public void FromEnvironment_fails_closed_when_provider_is_missing()
    {
        var previousProvider = Environment.GetEnvironmentVariable(OpenAiGenerationModel.ProviderEnvironmentVariable);
        var previousModel = Environment.GetEnvironmentVariable(OpenAiGenerationModel.ModelEnvironmentVariable);
        var previousKey = Environment.GetEnvironmentVariable(OpenAiGenerationModel.ApiKeyEnvironmentVariable);

        try
        {
            Environment.SetEnvironmentVariable(OpenAiGenerationModel.ProviderEnvironmentVariable, null);
            Environment.SetEnvironmentVariable(OpenAiGenerationModel.ModelEnvironmentVariable, null);
            Environment.SetEnvironmentVariable(OpenAiGenerationModel.ApiKeyEnvironmentVariable, null);

            Assert.Throws<ArgumentException>(() => OpenAiGenerationModel.FromEnvironment());
        }
        finally
        {
            Environment.SetEnvironmentVariable(OpenAiGenerationModel.ProviderEnvironmentVariable, previousProvider);
            Environment.SetEnvironmentVariable(OpenAiGenerationModel.ModelEnvironmentVariable, previousModel);
            Environment.SetEnvironmentVariable(OpenAiGenerationModel.ApiKeyEnvironmentVariable, previousKey);
        }
    }

    [Fact]
    public void FromEnvironment_fails_closed_when_api_key_is_missing()
    {
        var previousProvider = Environment.GetEnvironmentVariable(OpenAiGenerationModel.ProviderEnvironmentVariable);
        var previousModel = Environment.GetEnvironmentVariable(OpenAiGenerationModel.ModelEnvironmentVariable);
        var previousKey = Environment.GetEnvironmentVariable(OpenAiGenerationModel.ApiKeyEnvironmentVariable);

        try
        {
            Environment.SetEnvironmentVariable(OpenAiGenerationModel.ProviderEnvironmentVariable, OpenAiGenerationModel.ProviderName);
            Environment.SetEnvironmentVariable(OpenAiGenerationModel.ModelEnvironmentVariable, "gpt-5.6");
            Environment.SetEnvironmentVariable(OpenAiGenerationModel.ApiKeyEnvironmentVariable, null);

            Assert.Throws<InvalidOperationException>(() => OpenAiGenerationModel.FromEnvironment());
        }
        finally
        {
            Environment.SetEnvironmentVariable(OpenAiGenerationModel.ProviderEnvironmentVariable, previousProvider);
            Environment.SetEnvironmentVariable(OpenAiGenerationModel.ModelEnvironmentVariable, previousModel);
            Environment.SetEnvironmentVariable(OpenAiGenerationModel.ApiKeyEnvironmentVariable, previousKey);
        }
    }

    [Fact]
    public async Task GenerateAsync_sends_correct_responses_payload_and_parses_rest_output_shape()
    {
        var handler = new RecordingHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(CreateValidResponse(), Encoding.UTF8, "application/json")
        });
        var httpClient = new HttpClient(handler);
        var model = new OpenAiGenerationModel(httpClient, "gpt-5.6");
        var context = CreateContext();
        var previousKey = Environment.GetEnvironmentVariable(OpenAiGenerationModel.ApiKeyEnvironmentVariable);

        try
        {
            Environment.SetEnvironmentVariable(OpenAiGenerationModel.ApiKeyEnvironmentVariable, "secret-key");

            var output = await model.GenerateAsync(context, CancellationToken.None);

            Assert.False(output.Abstained);
            Assert.Single(output.Claims);
            Assert.Contains("Question:", handler.RequestBody);
            Assert.Contains("Evidence:", handler.RequestBody);
            Assert.DoesNotContain("guidelines", handler.RequestBody, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("model_answer", handler.RequestBody, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("oracle", handler.RequestBody, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("secret-key", handler.RequestBody, StringComparison.OrdinalIgnoreCase);

            using var requestJson = JsonDocument.Parse(handler.RequestBody);
            var root = requestJson.RootElement;
            Assert.Equal("gpt-5.6", root.GetProperty("model").GetString());
            Assert.False(root.TryGetProperty("seed", out _));

            var format = root.GetProperty("text").GetProperty("format");
            Assert.Equal("json_schema", format.GetProperty("type").GetString());
            Assert.Equal("rj_generation_response", format.GetProperty("name").GetString());
            Assert.True(format.TryGetProperty("schema", out var schema));
            Assert.False(format.TryGetProperty("json_schema", out _));

            var abstentionReason = schema.GetProperty("properties").GetProperty("abstention_reason");
            Assert.True(abstentionReason.TryGetProperty("anyOf", out var anyOf));
            Assert.Equal(2, anyOf.GetArrayLength());
        }
        finally
        {
            Environment.SetEnvironmentVariable(OpenAiGenerationModel.ApiKeyEnvironmentVariable, previousKey);
        }
    }

    [Fact]
    public async Task GenerateAsync_reports_json_deserialization_stage_for_http_200_with_invalid_output_text()
    {
        var handler = new RecordingHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(CreateResponseWithText("not-json"), Encoding.UTF8, "application/json")
        });
        var model = new OpenAiGenerationModel(new HttpClient(handler), "gpt-5.6");
        var context = CreateContext();
        var previousKey = Environment.GetEnvironmentVariable(OpenAiGenerationModel.ApiKeyEnvironmentVariable);
        var previousDiagnostics = Environment.GetEnvironmentVariable(OpenAiGenerationModel.DiagnosticsEnvironmentVariable);
        var previousError = Console.Error;
        using var diagnosticWriter = new StringWriter();

        try
        {
            Environment.SetEnvironmentVariable(OpenAiGenerationModel.ApiKeyEnvironmentVariable, "secret-key");
            Environment.SetEnvironmentVariable(OpenAiGenerationModel.DiagnosticsEnvironmentVariable, "1");
            Console.SetError(diagnosticWriter);

            await Assert.ThrowsAsync<OpenAiAdapterException>(() => model.GenerateAsync(context, CancellationToken.None));

            var diagnostic = diagnosticWriter.ToString();
            Assert.Contains("OPENAI_DIAGNOSTIC", diagnostic, StringComparison.Ordinal);
            Assert.Contains("stage=JSON_DESERIALIZATION", diagnostic, StringComparison.Ordinal);
            Assert.Contains("outputItemCount=1", diagnostic, StringComparison.Ordinal);
            Assert.Contains("contentItemTypes=output_text", diagnostic, StringComparison.Ordinal);
            Assert.Contains("hasOutputText=True", diagnostic, StringComparison.Ordinal);
            Assert.DoesNotContain("secret-key", diagnostic, StringComparison.Ordinal);
            Assert.DoesNotContain("not-json", diagnostic, StringComparison.Ordinal);
        }
        finally
        {
            Console.SetError(previousError);
            Environment.SetEnvironmentVariable(OpenAiGenerationModel.ApiKeyEnvironmentVariable, previousKey);
            Environment.SetEnvironmentVariable(OpenAiGenerationModel.DiagnosticsEnvironmentVariable, previousDiagnostics);
        }
    }

    [Fact]
    public async Task GenerateAsync_reports_structured_output_extraction_stage_when_output_text_is_missing()
    {
        var handler = new RecordingHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""
            {
              "id": "resp-test",
              "object": "response",
              "status": "completed",
              "output": [
                {
                  "type": "message",
                  "role": "assistant",
                  "status": "completed",
                  "content": [
                    { "type": "refusal", "refusal": "cannot answer" }
                  ]
                }
              ]
            }
            """, Encoding.UTF8, "application/json")
        });
        var model = new OpenAiGenerationModel(new HttpClient(handler), "gpt-5.6");
        var context = CreateContext();
        var previousKey = Environment.GetEnvironmentVariable(OpenAiGenerationModel.ApiKeyEnvironmentVariable);
        var previousDiagnostics = Environment.GetEnvironmentVariable(OpenAiGenerationModel.DiagnosticsEnvironmentVariable);
        var previousError = Console.Error;
        using var diagnosticWriter = new StringWriter();

        try
        {
            Environment.SetEnvironmentVariable(OpenAiGenerationModel.ApiKeyEnvironmentVariable, "secret-key");
            Environment.SetEnvironmentVariable(OpenAiGenerationModel.DiagnosticsEnvironmentVariable, "1");
            Console.SetError(diagnosticWriter);

            await Assert.ThrowsAsync<OpenAiAdapterException>(() => model.GenerateAsync(context, CancellationToken.None));

            var diagnostic = diagnosticWriter.ToString();
            Assert.Contains("stage=STRUCTURED_OUTPUT_EXTRACTION", diagnostic, StringComparison.Ordinal);
            Assert.Contains("contentItemTypes=refusal", diagnostic, StringComparison.Ordinal);
            Assert.Contains("hasOutputText=False", diagnostic, StringComparison.Ordinal);
            Assert.DoesNotContain("cannot answer", diagnostic, StringComparison.Ordinal);
        }
        finally
        {
            Console.SetError(previousError);
            Environment.SetEnvironmentVariable(OpenAiGenerationModel.ApiKeyEnvironmentVariable, previousKey);
            Environment.SetEnvironmentVariable(OpenAiGenerationModel.DiagnosticsEnvironmentVariable, previousDiagnostics);
        }
    }

    [Fact]
    public async Task GenerateAsync_rejects_citation_outside_context_as_local_validation_failure()
    {
        const string invalidOutput = "{\"abstained\":false,\"abstention_reason\":null,\"claims\":[{\"text\":\"A tutela foi deferida.\",\"citations\":[{\"documentId\":\"doc-x\",\"contentSha256\":\"ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff\",\"startOffset\":0,\"length\":1}]}]}";
        var handler = new RecordingHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(CreateResponseWithText(invalidOutput), Encoding.UTF8, "application/json")
        });
        var model = new OpenAiGenerationModel(new HttpClient(handler), "gpt-5.6");
        var context = CreateContext();
        var previousKey = Environment.GetEnvironmentVariable(OpenAiGenerationModel.ApiKeyEnvironmentVariable);
        var previousDiagnostics = Environment.GetEnvironmentVariable(OpenAiGenerationModel.DiagnosticsEnvironmentVariable);
        var previousError = Console.Error;
        using var diagnosticWriter = new StringWriter();

        try
        {
            Environment.SetEnvironmentVariable(OpenAiGenerationModel.ApiKeyEnvironmentVariable, "secret-key");
            Environment.SetEnvironmentVariable(OpenAiGenerationModel.DiagnosticsEnvironmentVariable, "1");
            Console.SetError(diagnosticWriter);

            await Assert.ThrowsAsync<OpenAiAdapterException>(() => model.GenerateAsync(context, CancellationToken.None));
            Assert.Contains("stage=LOCAL_VALIDATION", diagnosticWriter.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            Console.SetError(previousError);
            Environment.SetEnvironmentVariable(OpenAiGenerationModel.ApiKeyEnvironmentVariable, previousKey);
            Environment.SetEnvironmentVariable(OpenAiGenerationModel.DiagnosticsEnvironmentVariable, previousDiagnostics);
        }
    }

    private static string CreateValidResponse()
    {
        const string output = "{\"abstained\":false,\"abstention_reason\":null,\"claims\":[{\"text\":\"A tutela foi deferida.\",\"citations\":[{\"documentId\":\"doc-1\",\"contentSha256\":\"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\",\"startOffset\":0,\"length\":23}]}]}";
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
                content = new object[]
                {
                    new { type = "output_text", text, annotations = Array.Empty<object>() }
                }
            }
        }
    });

    private static GenerationContext CreateContext()
    {
        const string excerpt = "A tutela foi deferida.";
        var position = SourcePosition.Create(0, excerpt.Length, excerpt.Length);
        var item = new GenerationContextItem(
            "case-1",
            "doc-1",
            "source.txt",
            new string('a', 64),
            excerpt,
            position,
            1f);

        return new GenerationContext("case-1", "Qual foi a decisão?", 1000, excerpt.Length, [item]);
    }

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
