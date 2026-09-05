using RJ.Application.Benchmarking;
using RJ.Application.Evaluation;
using RJ.Application.Generation;
using System.Text;

namespace RJ.BenchmarkCli;

public static class BenchmarkCli
{
    public const int SuccessExitCode = 0;
    public const int GateFailureExitCode = 1;
    public const int UsageOrExecutionErrorExitCode = 2;

    public static Task<int> RunAsync(
        IReadOnlyList<string> args,
        CancellationToken cancellationToken) =>
        RunAsync(args, Console.Error, cancellationToken);

    public static async Task<int> RunAsync(
        IReadOnlyList<string> args,
        TextWriter errorWriter,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(errorWriter);

        try
        {
            var options = BenchmarkCliOptions.Parse(args);
            var demoCorpusRoot = Environment.GetEnvironmentVariable(OabBenchDemoCatalogAdapter.EnvironmentVariable);
            var model = CreateModel(options.ModelId, options.ModelConfiguration, !string.IsNullOrWhiteSpace(demoCorpusRoot));

            var catalog = options.CatalogPath is null
                ? await LoadCatalogAsync(options, cancellationToken)
                : await LoadExternalCatalogAsync(options, cancellationToken);

            return await ExecuteAsync(
                options,
                catalog,
                model,
                BuildCommand(args),
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (ArgumentException exception)
        {
            await errorWriter.WriteLineAsync($"{exception.GetType().Name}: {exception.Message}");
            return UsageOrExecutionErrorExitCode;
        }
        catch (Exception exception)
        {
            await errorWriter.WriteLineAsync($"{exception.GetType().Name}: Benchmark execution failed.");
            return UsageOrExecutionErrorExitCode;
        }
    }

    private static IGenerationModel CreateModel(string modelId, string modelConfiguration, bool hasDemoCorpus)
    {
        if (StringComparer.Ordinal.Equals(modelId, HarnessSelfTestGenerationModel.ModelId))
        {
            return new HarnessSelfTestGenerationModel();
        }

        if (StringComparer.Ordinal.Equals(modelId, OabBenchDemoGenerationModel.ModelId))
        {
            if (!hasDemoCorpus)
            {
                throw new ArgumentException(
                    $"Model-id '{OabBenchDemoGenerationModel.ModelId}' requires an external demo corpus via {OabBenchDemoCatalogAdapter.EnvironmentVariable}.",
                    nameof(modelId));
            }

            return new OabBenchDemoGenerationModel();
        }

        if (StringComparer.Ordinal.Equals(modelId, OabRulingBrGenerationChallengerModel.ModelId))
        {
            return new OabRulingBrGenerationChallengerModel(modelConfiguration);
        }

        throw new ArgumentException(
            $"Unsupported model-id '{modelId}'. Supported ids are '{HarnessSelfTestGenerationModel.ModelId}', '{OabBenchDemoGenerationModel.ModelId}' and '{OabRulingBrGenerationChallengerModel.ModelId}'.",
            nameof(modelId));
    }

    private static async Task<GenerationBenchmarkCatalog> LoadCatalogAsync(
        BenchmarkCliOptions options,
        CancellationToken cancellationToken)
    {
        var corpusRoot = Environment.GetEnvironmentVariable(OabBenchDemoCatalogAdapter.EnvironmentVariable);
        if (string.IsNullOrWhiteSpace(corpusRoot))
        {
            return ApprovedGenerationBenchmarkCatalog.Create();
        }

        var adapter = await OabBenchDemoCatalogAdapter.BuildAsync(corpusRoot, options.GitCommit, cancellationToken);
        var external = ExternalGenerationBenchmarkCatalog.Parse(await File.ReadAllBytesAsync(adapter.CatalogPath, cancellationToken));
        return external.ToBenchmarkCatalog();
    }

    public static async Task<int> ExecuteAsync(
        BenchmarkCliOptions options,
        GenerationBenchmarkCatalog catalog,
        IGenerationModel model,
        string command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(command);

        var runner = new GenerationBenchmarkRunner(model, new GenerationEvaluator());
            var report = await runner.RunAsync(catalog, options.ToMetadata(catalog.Version), cancellationToken);
            var json = GenerationBenchmarkJson.Serialize(report);
        var reportSha256 = ComputeSha256(Encoding.UTF8.GetBytes(json));
        var manifest = new BenchmarkRunManifest(
            BenchmarkRunManifestJson.CurrentVersion,
            options.GitCommit,
            options.Runtime,
            catalog.Version,
            options.ModelId,
            options.ModelConfiguration,
            options.Seed,
            command,
            options.OutputPath,
            reportSha256,
            report.Passed ? SuccessExitCode : GateFailureExitCode,
            report.Passed);

        await AtomicTextFileWriter.WriteAsync(options.OutputPath, json, cancellationToken);
        await AtomicTextFileWriter.WriteAsync(
            Path.ChangeExtension(options.OutputPath, ".run-manifest.json"),
            BenchmarkRunManifestJson.Serialize(manifest),
            cancellationToken);
        return report.Passed ? SuccessExitCode : GateFailureExitCode;
    }

    private static async Task<GenerationBenchmarkCatalog> LoadExternalCatalogAsync(
        BenchmarkCliOptions options,
        CancellationToken cancellationToken)
    {
        var bytes = await File.ReadAllBytesAsync(options.CatalogPath!, cancellationToken);
        var actualSha256 = ExternalGenerationBenchmarkCatalog.ComputeSha256(bytes);
        if (!StringComparer.Ordinal.Equals(actualSha256, options.CatalogSha256))
        {
            throw new InvalidOperationException("External benchmark catalog checksum mismatch.");
        }

        var external = ExternalGenerationBenchmarkCatalog.Parse(bytes);
        return external.ToBenchmarkCatalog();
    }

    private static string BuildCommand(IReadOnlyList<string> args) =>
        string.Join(" ", args.Select(QuoteArgument));

    private static string QuoteArgument(string argument) =>
        argument.Any(character => char.IsWhiteSpace(character) || character == '"')
            ? $"\"{argument.Replace("\"", "\\\"")}\""
            : argument;

    private static string ComputeSha256(byte[] bytes) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant();
}
