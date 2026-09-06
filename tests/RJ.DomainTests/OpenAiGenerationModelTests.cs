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
        var provider = Environment.GetEnvironmentVariable(OpenAiGenerationModel.ProviderEnvironmentVariable);
        var model = Environment.GetEnvironmentVariable(OpenAiGenerationModel.ModelEnvironmentVariable);
        var key = Environment.GetEnvironmentVariable(OpenAiGenerationModel.ApiKeyEnvironmentVariable);
        try
        {
            Environment.SetEnvironmentVariable(OpenAiGenerationModel.ProviderEnvironmentVariable, null);
            Environment.SetEnvironmentVariable(OpenAiGenerationModel.ModelEnvironmentVariable, null);
            Environment.SetEnvironmentVariable(OpenAiGenerationModel.ApiKeyEnvironmentVariable, null);
            Assert.Throws<ArgumentException>(() => OpenAiGenerationModel.FromEnvironment());
        }
        finally
        {
            Environment.SetEnvironmentVariable(OpenAiGenerationModel.ProviderEnvironmentVariable, provider);
            Environment.SetEnvironmentVariable(OpenAiGenerationModel.ModelEnvironmentVariable, model);
            Environment.SetEnvironmentVariable(OpenAiGenerationModel.ApiKeyEnvironmentVariable, key);
        }
    }

    [Fact]
    public void FromEnvironment_fails_closed_when_api_key_is_missing()
    {
        var provider = Environment.GetEnvironmentVariable(OpenAiGenerationModel.ProviderEnvironmentVariable);
        var model = Environment.GetEnvironmentVariable(OpenAiGenerationModel.ModelEnvironmentVariable);
        var key = Environment.GetEnvironmentVariable(OpenAiGenerationModel.ApiKeyEnvironmentVariable);
        try
        {
            Environment.SetEnvironmentVariable(OpenAiGenerationModel.ProviderEnvironmentVariable, OpenAiGenerationModel.ProviderName);
            Environment.SetEnvironmentVariable(OpenAiGenerationModel.ModelEnvironmentVariable, "gpt-5.6");
            Environment.SetEnvironmentVariable(OpenAiGenerationModel.ApiKeyEnvironmentVariable, null);
            Assert.Throws<InvalidOperationException>(() => OpenAiGenerationModel.FromEnvironment());
        }
        finally
        {
            Environment.SetEnvironmentVariable(OpenAiGenerationModel.ProviderEnvironmentVariable, provider);
            Environment.SetEnvironmentVariable(OpenAiGenerationModel.ModelEnvironmentVariable, model);
            Environment.SetEnvironmentVariable(OpenAiGenerationModel.ApiKeyEnvironmentVariable, key);
        }
    }

    [Fact]
    public async Task GenerateAsync_sends_correct_schema_and_parses_raw_responses_shape()
    {
        var handler = new RecordingHandler(Ok(RawResponse(ValidStructuredOutput())));
        var adapter = new OpenAiGenerationModel(new HttpClient(handler), "gpt-5.6");

        var output = await WithKey(() => adapter.GenerateAsync(CreateContext(), CancellationToken.None));

        Assert.False(output.Abstained);
        Assert.Single(output.Claims);
        using var request = JsonDocument.Parse(handler.RequestBody);
        var root = request.RootElement;
        Assert.Equal("gpt-5.6", root.GetProperty("model").GetString());
        Assert.False(root.TryGetProperty("seed", out _));
        var format = root.GetProperty("text").GetProperty("format");
        Assert.Equal("json_schema", format.GetProperty("type").GetString());
        Assert.Equal("rj_generation_response", format.GetProperty("name").GetString());
        Assert.True(format.TryGetProperty("schema", out var schema));
        Assert.False(format.TryGetProperty("json_schema", out _));
        Assert.Equal(2, schema.GetProperty("properties").GetProperty("abstention_reason").GetProperty("anyOf").GetArrayLength());
        Assert.DoesNotContain("secret-key", handler.RequestBody, StringComparison.Ordinal);
        Assert.DoesNotContain("oracle", handler.RequestBody, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GenerateAsync_classifies_invalid_provider_json_as_response_parse()
    {
        var exception = await RunFailure(Ok("not-json"));
        Assert.Equal(OpenAiGenerationModel.ResponseParseStage, exception.Stage);
    }

    [Fact]
    public async Task GenerateAsync_classifies_missing_output_text_as_structured_output_extraction()
    {
        var exception = await RunFailure(Ok("""
        {"object":"response","output":[{"type":"message","content":[{"type":"refusal","refusal":"no"}]}]}
        """));
        Assert.Equal(OpenAiGenerationModel.StructuredOutputExtractionStage, exception.Stage);
        Assert.Equal("response", exception.ResponseObjectType);
        Assert.Equal(1, exception.OutputItemCount);
        Assert.Contains("refusal", exception.ContentItemTypes);
        Assert.False(exception.HasOutputText);
    }

    [Fact]
    public async Task GenerateAsync_classifies_invalid_structured_json_as_json_deserialization()
    {
        var exception = await RunFailure(Ok(RawResponse("not-json")));
        Assert.Equal(OpenAiGenerationModel.JsonDeserializationStage, exception.Stage);
        Assert.True(exception.HasOutputText);
    }

    [Fact]
    public async Task GenerateAsync_classifies_out_of_context_citation_as_local_validation()
    {
        const string invalidCitation = "{\"abstained\":false,\"abstention_reason\":null,\"claims\":[{\"text\":\"x\",\"citations\":[{\"documentId\":\"doc-x\",\"contentSha256\":\"ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff\",\"startOffset\":0,\"length\":1}]}]}";
        var exception = await RunFailure(Ok(RawResponse(invalidCitation)));
        Assert.Equal(OpenAiGenerationModel.LocalValidationStage, exception.Stage);
        Assert.True(exception.HasStructuredJson);
    }

    [Fact]
    public async Task GenerateAsync_preserves_safe_provider_error_metadata()
    {
        var response = new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent("{\"error\":{\"code\":\"invalid_json_schema\",\"message\":\"Schema rejected.\"}}", Encoding.UTF8, "application/json")
        };
        var exception = await RunFailure(response);
        Assert.Equal(OpenAiGenerationModel.HttpStage, exception.Stage);
        Assert.Equal(400, exception.HttpStatus);
        Assert.Equal("invalid_json_schema", exception.ProviderErrorCode);
        Assert.Equal("Schema rejected.", exception.ProviderErrorMessage);
        Assert.DoesNotContain("secret-key", exception.Message, StringComparison.Ordinal);
    }

    private static async Task<OpenAiAdapterException> RunFailure(HttpResponseMessage response)
    {
        var adapter = new OpenAiGenerationModel(new HttpClient(new RecordingHandler(response)), "gpt-5.6");
        return await Assert.ThrowsAsync<OpenAiAdapterException>(() => WithKey(() => adapter.GenerateAsync(CreateContext(), CancellationToken.None)));
    }

    private static async Task<T> WithKey<T>(Func<Task<T>> action)
    {
        var key = Environment.GetEnvironmentVariable(OpenAiGenerationModel.ApiKeyEnvironmentVariable);
        try
        {
            Environment.SetEnvironmentVariable(OpenAiGenerationModel.ApiKeyEnvironmentVariable, "secret-key");
            return await action();
        }
        finally
        {
            Environment.SetEnvironmentVariable(OpenAiGenerationModel.ApiKeyEnvironmentVariable, key);
        }
    }

    private static HttpResponseMessage Ok(string body) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json")
    };

    private static string RawResponse(string text) => JsonSerializer.Serialize(new
    {
        id = "resp_test",
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

    private static string ValidStructuredOutput() =>
        "{\"abstained\":false,\"abstention_reason\":null,\"claims\":[{\"text\":\"A tutela foi deferida.\",\"citations\":[{\"documentId\":\"doc-1\",\"contentSha256\":\"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\",\"startOffset\":0,\"length\":22}]}]}";

    private static GenerationContext CreateContext()
    {
        const string excerpt = "A tutela foi deferida.";
        var item = new GenerationContextItem("case-1", "doc-1", "source.txt", new string('a', 64), excerpt,
            SourcePosition.Create(0, excerpt.Length, excerpt.Length), 1f);
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
