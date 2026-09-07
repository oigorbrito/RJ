using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using RJ.Application.Generation;
using RJ.Application.Retrieval;
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
            Environment.SetEnvironmentVariable(OpenAiGenerationModel.ModelEnvironmentVariable, OpenAiGenerationModel.ModelId);
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
    public async Task GenerateAsync_sends_only_context_evidence_and_parses_structured_output()
    {
        var handler = new RecordingHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""
            {
              "output_text": "{\"abstained\":false,\"abstention_reason\":null,\"claims\":[{\"text\":\"A tutela foi deferida.\",\"citations\":[{\"documentId\":\"doc-1\",\"contentSha256\":\"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\",\"startOffset\":0,\"length\":22}]}]}"
            }
            """, Encoding.UTF8, "application/json")
        });
        var httpClient = new HttpClient(handler);
        var previousProvider = Environment.GetEnvironmentVariable(OpenAiGenerationModel.ProviderEnvironmentVariable);
        var previousModel = Environment.GetEnvironmentVariable(OpenAiGenerationModel.ModelEnvironmentVariable);
        var previousKey = Environment.GetEnvironmentVariable(OpenAiGenerationModel.ApiKeyEnvironmentVariable);
        var context = CreateContext();

        try
        {
            Environment.SetEnvironmentVariable(OpenAiGenerationModel.ProviderEnvironmentVariable, OpenAiGenerationModel.ProviderName);
            Environment.SetEnvironmentVariable(OpenAiGenerationModel.ModelEnvironmentVariable, "gpt-4.1-mini");
            Environment.SetEnvironmentVariable(OpenAiGenerationModel.ApiKeyEnvironmentVariable, "secret-key");

            var model = OpenAiGenerationModel.FromEnvironment(httpClient);
            var output = await model.GenerateAsync(context, CancellationToken.None);

            Assert.False(output.Abstained);
            Assert.Single(output.Claims);
            Assert.Contains("\"model\":\"gpt-4.1-mini\"", handler.RequestBody, StringComparison.Ordinal);
            Assert.Contains("\"name\":\"rj_generation_response\"", handler.RequestBody, StringComparison.Ordinal);
            Assert.Contains("\"schema\":{", handler.RequestBody, StringComparison.Ordinal);
            Assert.DoesNotContain("\"seed\"", handler.RequestBody, StringComparison.Ordinal);
            Assert.DoesNotContain("\"json_schema\":", handler.RequestBody, StringComparison.Ordinal);
            Assert.Contains("Question:", handler.RequestBody);
            Assert.Contains("Evidence:", handler.RequestBody);
            Assert.DoesNotContain("secret-key", handler.RequestBody, StringComparison.OrdinalIgnoreCase);

            using var document = JsonDocument.Parse(handler.RequestBody);
            var root = document.RootElement;
            var schema = root.GetProperty("text").GetProperty("format").GetProperty("schema");
            Assert.Equal("object", schema.GetProperty("type").GetString());
            Assert.Equal("rj_generation_response", root.GetProperty("text").GetProperty("format").GetProperty("name").GetString());
            Assert.True(schema.TryGetProperty("properties", out var properties));
            var abstentionReason = properties.GetProperty("abstention_reason");
            Assert.True(abstentionReason.TryGetProperty("anyOf", out var anyOf));
            Assert.Equal(2, anyOf.GetArrayLength());
            Assert.Equal("string", anyOf[0].GetProperty("type").GetString());
            Assert.Equal("null", anyOf[1].GetProperty("type").GetString());
            Assert.False(abstentionReason.TryGetProperty("type", out _));
        }
        finally
        {
            Environment.SetEnvironmentVariable(OpenAiGenerationModel.ProviderEnvironmentVariable, previousProvider);
            Environment.SetEnvironmentVariable(OpenAiGenerationModel.ModelEnvironmentVariable, previousModel);
            Environment.SetEnvironmentVariable(OpenAiGenerationModel.ApiKeyEnvironmentVariable, previousKey);
        }
    }

    [Fact]
    public async Task GenerateAsync_parses_responses_api_output_array_shape()
    {
        var handler = new RecordingHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""
            {
              "output": [
                {
                  "type": "message",
                  "role": "assistant",
                  "content": [
                    {
                      "type": "output_text",
                      "text": "{\"abstained\":false,\"abstention_reason\":null,\"claims\":[{\"text\":\"A tutela foi deferida.\",\"citations\":[{\"documentId\":\"doc-1\",\"contentSha256\":\"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\",\"startOffset\":0,\"length\":22}]}]}"
                    }
                  ]
                }
              ]
            }
            """, Encoding.UTF8, "application/json")
        });
        var httpClient = new HttpClient(handler);
        var previousProvider = Environment.GetEnvironmentVariable(OpenAiGenerationModel.ProviderEnvironmentVariable);
        var previousModel = Environment.GetEnvironmentVariable(OpenAiGenerationModel.ModelEnvironmentVariable);
        var previousKey = Environment.GetEnvironmentVariable(OpenAiGenerationModel.ApiKeyEnvironmentVariable);
        var context = CreateContext();

        try
        {
            Environment.SetEnvironmentVariable(OpenAiGenerationModel.ProviderEnvironmentVariable, OpenAiGenerationModel.ProviderName);
            Environment.SetEnvironmentVariable(OpenAiGenerationModel.ModelEnvironmentVariable, "gpt-4.1-mini");
            Environment.SetEnvironmentVariable(OpenAiGenerationModel.ApiKeyEnvironmentVariable, "secret-key");

            var model = OpenAiGenerationModel.FromEnvironment(httpClient);
            var output = await model.GenerateAsync(context, CancellationToken.None);

            Assert.False(output.Abstained);
            Assert.Single(output.Claims);
        }
        finally
        {
            Environment.SetEnvironmentVariable(OpenAiGenerationModel.ProviderEnvironmentVariable, previousProvider);
            Environment.SetEnvironmentVariable(OpenAiGenerationModel.ModelEnvironmentVariable, previousModel);
            Environment.SetEnvironmentVariable(OpenAiGenerationModel.ApiKeyEnvironmentVariable, previousKey);
        }
    }

    [Fact]
    public async Task GenerateAsync_rejects_malformed_response()
    {
        var handler = new RecordingHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"output_text":"not-json"}""", Encoding.UTF8, "application/json")
        });
        var httpClient = new HttpClient(handler);
        var previousProvider = Environment.GetEnvironmentVariable(OpenAiGenerationModel.ProviderEnvironmentVariable);
        var previousModel = Environment.GetEnvironmentVariable(OpenAiGenerationModel.ModelEnvironmentVariable);
        var previousKey = Environment.GetEnvironmentVariable(OpenAiGenerationModel.ApiKeyEnvironmentVariable);
        var context = CreateContext();

        try
        {
            Environment.SetEnvironmentVariable(OpenAiGenerationModel.ProviderEnvironmentVariable, OpenAiGenerationModel.ProviderName);
            Environment.SetEnvironmentVariable(OpenAiGenerationModel.ModelEnvironmentVariable, "gpt-4.1-mini");
            Environment.SetEnvironmentVariable(OpenAiGenerationModel.ApiKeyEnvironmentVariable, "secret-key");

            var model = OpenAiGenerationModel.FromEnvironment(httpClient);
            await Assert.ThrowsAsync<global::RJ.Application.Benchmarking.OpenAiAdapterException>(() => model.GenerateAsync(context, CancellationToken.None));
        }
        finally
        {
            Environment.SetEnvironmentVariable(OpenAiGenerationModel.ProviderEnvironmentVariable, previousProvider);
            Environment.SetEnvironmentVariable(OpenAiGenerationModel.ModelEnvironmentVariable, previousModel);
            Environment.SetEnvironmentVariable(OpenAiGenerationModel.ApiKeyEnvironmentVariable, previousKey);
        }
    }

    [Fact]
    public async Task GenerateAsync_rejects_citation_outside_context()
    {
        var handler = new RecordingHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""
            {
              "output_text": "{\"abstained\":false,\"abstention_reason\":null,\"claims\":[{\"text\":\"A tutela foi deferida.\",\"citations\":[{\"documentId\":\"doc-x\",\"contentSha256\":\"ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff\",\"startOffset\":0,\"length\":1}]}]}"
            }
            """, Encoding.UTF8, "application/json")
        });
        var httpClient = new HttpClient(handler);
        var previousProvider = Environment.GetEnvironmentVariable(OpenAiGenerationModel.ProviderEnvironmentVariable);
        var previousModel = Environment.GetEnvironmentVariable(OpenAiGenerationModel.ModelEnvironmentVariable);
        var previousKey = Environment.GetEnvironmentVariable(OpenAiGenerationModel.ApiKeyEnvironmentVariable);
        var context = CreateContext();

        try
        {
            Environment.SetEnvironmentVariable(OpenAiGenerationModel.ProviderEnvironmentVariable, OpenAiGenerationModel.ProviderName);
            Environment.SetEnvironmentVariable(OpenAiGenerationModel.ModelEnvironmentVariable, "gpt-4.1-mini");
            Environment.SetEnvironmentVariable(OpenAiGenerationModel.ApiKeyEnvironmentVariable, "secret-key");

            var model = OpenAiGenerationModel.FromEnvironment(httpClient);
            await Assert.ThrowsAsync<global::RJ.Application.Benchmarking.OpenAiAdapterException>(() => model.GenerateAsync(context, CancellationToken.None));
        }
        finally
        {
            Environment.SetEnvironmentVariable(OpenAiGenerationModel.ProviderEnvironmentVariable, previousProvider);
            Environment.SetEnvironmentVariable(OpenAiGenerationModel.ModelEnvironmentVariable, previousModel);
            Environment.SetEnvironmentVariable(OpenAiGenerationModel.ApiKeyEnvironmentVariable, previousKey);
        }
    }

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
