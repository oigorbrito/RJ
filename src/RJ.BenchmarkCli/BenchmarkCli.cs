using RJ.Application.Benchmarking;
using RJ.Application.Evaluation;
using RJ.Application.Generation;

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
            if (!StringComparer.Ordinal.Equals(options.ModelId, HarnessSelfTestGenerationModel.ModelId))
            {
                throw new ArgumentException(
                    $"Only model-id '{HarnessSelfTestGenerationModel.ModelId}' is available in this harness phase.",
                    nameof(args));
            }

            var catalog = options.CatalogPath is null
                ? ApprovedGenerationBenchmarkCatalog.Create()
                : await LoadExternalCatalogAsync(options, cancellationToken);

            return await ExecuteAsync(
                options,
                catalog,
                new HarnessSelfTestGenerationModel(),
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
        var manifest = new BenchmarkRunManifest(
            options.GitCommit,
            options.Runtime,
            catalog.Version,
            options.ModelId,
            options.ModelConfiguration,
            options.Seed,
            command,
            options.OutputPath,
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
}
