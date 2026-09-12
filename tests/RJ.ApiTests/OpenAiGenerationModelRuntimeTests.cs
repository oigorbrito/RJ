using System.Net;
using System.Text;
using RJ.Application.Generation;
using RJ.Application.Retrieval;
using RJ.Infrastructure.Generation;

namespace RJ.ApiTests;

[Collection("Generation environment variables")]
public sealed class OpenAiGenerationModelRuntimeTests
{
    [Fact]
    public async Task GenerateAsync_parses_structured_output_and_preserves_citation_boundary()
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""
            {
              "output_text": "{\"abstained\":false,\"abstention_reason\":null,\"claims\":[{\"text\":\"A tutela foi deferida.\",\"citations\":[{\"documentId\":\"doc-1\",\"contentSha256\":\"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\",\"startOffset\":0,\"length\":22}]}]}"
            }
            """, Encoding.UTF8, "application/json")
        };
        var handler = new StubHandler(response);
        using var client = new HttpClient(handler);
        var model = new OpenAiGenerationModel(client, "test-model");
        var previousKey = Environment.GetEnvironmentVariable(OpenAiGenerationModel.ApiKeyEnvironmentVariable);

        try
        {
            Environment.SetEnvironmentVariable(OpenAiGenerationModel.ApiKeyEnvironmentVariable, "test-key");
            var output = await model.GenerateAsync(CreateContext(), CancellationToken.None);

            Assert.False(output.Abstained);
            Assert.Single(output.Claims);
            Assert.Single(output.Claims[0].Citations);
            Assert.Contains("Question:", handler.RequestBody, StringComparison.Ordinal);
            Assert.Contains("Evidence:", handler.RequestBody, StringComparison.Ordinal);
            Assert.DoesNotContain("test-key", handler.RequestBody, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable(OpenAiGenerationModel.ApiKeyEnvironmentVariable, previousKey);
        }
    }

    [Fact]
    public async Task GenerateAsync_rejects_citation_not_present_in_context()
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""
            {
              "output_text": "{\"abstained\":false,\"abstention_reason\":null,\"claims\":[{\"text\":\"A tutela foi deferida.\",\"citations\":[{\"documentId\":\"doc-x\",\"contentSha256\":\"ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff\",\"startOffset\":0,\"length\":1}]}]}"
            }
            """, Encoding.UTF8, "application/json")
        };
        using var client = new HttpClient(new StubHandler(response));
        var model = new OpenAiGenerationModel(client, "test-model");
        var previousKey = Environment.GetEnvironmentVariable(OpenAiGenerationModel.ApiKeyEnvironmentVariable);

        try
        {
            Environment.SetEnvironmentVariable(OpenAiGenerationModel.ApiKeyEnvironmentVariable, "test-key");
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => model.GenerateAsync(CreateContext(), CancellationToken.None));
        }
        finally
        {
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

    private sealed class StubHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        public string RequestBody { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestBody = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);
            return response;
        }
    }
}
