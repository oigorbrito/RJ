using RJ.Api;
using RJ.Application.Generation;
using RJ.Infrastructure.Generation;

namespace RJ.ApiTests;

public sealed class GenerationModelProviderTests
{
    [Fact]
    public void Create_defaults_to_deterministic_provider()
    {
        using var scope = new EnvironmentScope(
            (GenerationModelProvider.ProviderEnvironmentVariable, null),
            (OpenAiGenerationModel.ModelEnvironmentVariable, null),
            (OpenAiGenerationModel.ApiKeyEnvironmentVariable, null));

        var model = GenerationModelProvider.Create();

        Assert.IsType<DeterministicProcessSummaryModel>(model);
    }

    [Fact]
    public void Create_uses_explicit_deterministic_provider()
    {
        using var scope = new EnvironmentScope(
            (GenerationModelProvider.ProviderEnvironmentVariable, GenerationModelProvider.DeterministicProviderName));

        var model = GenerationModelProvider.Create();

        Assert.IsType<DeterministicProcessSummaryModel>(model);
    }

    [Fact]
    public void Create_fails_closed_for_unknown_provider()
    {
        using var scope = new EnvironmentScope(
            (GenerationModelProvider.ProviderEnvironmentVariable, "unknown-provider"));

        var exception = Assert.Throws<InvalidOperationException>(() => GenerationModelProvider.Create());

        Assert.Contains("Unsupported generation provider", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Create_fails_closed_for_openai_without_model()
    {
        using var scope = new EnvironmentScope(
            (GenerationModelProvider.ProviderEnvironmentVariable, OpenAiGenerationModel.ProviderName),
            (OpenAiGenerationModel.ModelEnvironmentVariable, null),
            (OpenAiGenerationModel.ApiKeyEnvironmentVariable, "test-key"));

        var exception = Assert.Throws<InvalidOperationException>(() => GenerationModelProvider.Create());

        Assert.Contains(OpenAiGenerationModel.ModelEnvironmentVariable, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Create_fails_closed_for_openai_without_api_key()
    {
        using var scope = new EnvironmentScope(
            (GenerationModelProvider.ProviderEnvironmentVariable, OpenAiGenerationModel.ProviderName),
            (OpenAiGenerationModel.ModelEnvironmentVariable, "test-model"),
            (OpenAiGenerationModel.ApiKeyEnvironmentVariable, null));

        var exception = Assert.Throws<InvalidOperationException>(() => GenerationModelProvider.Create());

        Assert.Contains(OpenAiGenerationModel.ApiKeyEnvironmentVariable, exception.Message, StringComparison.Ordinal);
    }

    private sealed class EnvironmentScope : IDisposable
    {
        private readonly Dictionary<string, string?> previous = new(StringComparer.Ordinal);

        public EnvironmentScope(params (string Name, string? Value)[] variables)
        {
            foreach (var (name, value) in variables)
            {
                previous[name] = Environment.GetEnvironmentVariable(name);
                Environment.SetEnvironmentVariable(name, value);
            }
        }

        public void Dispose()
        {
            foreach (var pair in previous)
            {
                Environment.SetEnvironmentVariable(pair.Key, pair.Value);
            }
        }
    }
}
