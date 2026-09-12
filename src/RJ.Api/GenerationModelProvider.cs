using RJ.Application.Generation;
using RJ.Infrastructure.Generation;

namespace RJ.Api;

public static class GenerationModelProvider
{
    public const string DeterministicProviderName = "deterministic";
    public const string ProviderEnvironmentVariable = "RJ_GENERATION_PROVIDER";

    public static IGenerationModel Create()
    {
        var provider = Environment.GetEnvironmentVariable(ProviderEnvironmentVariable)?.Trim();

        if (string.IsNullOrWhiteSpace(provider)
            || StringComparer.OrdinalIgnoreCase.Equals(provider, DeterministicProviderName))
        {
            return new DeterministicProcessSummaryModel();
        }

        if (StringComparer.OrdinalIgnoreCase.Equals(provider, OpenAiGenerationModel.ProviderName))
        {
            return OpenAiGenerationModel.FromEnvironment();
        }

        throw new InvalidOperationException(
            $"Unsupported generation provider '{provider}'. Supported values: deterministic, openai.");
    }

    public static string DescribeConfiguredProvider()
    {
        var provider = Environment.GetEnvironmentVariable(ProviderEnvironmentVariable)?.Trim();
        return string.IsNullOrWhiteSpace(provider) ? DeterministicProviderName : provider;
    }
}
