using RJ.Application.Benchmarking;
using RJ.Application.Evaluation;
using RJ.Application.Generation;

namespace RJ.BenchmarkCli;

public static class BenchmarkCli
{
    public const int SuccessExitCode = 0;
    public const int GateFailureExitCode = 1;
    public const int UsageOrExecutionErrorExitCode = 2;

    public static async Task<int> RunAsync(
        IReadOnlyList<string> args,
        CancellationToken cancellationToken)
    {
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
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"{exception.GetType().Name}: {exception.Message}");
            return UsageOrExecutionErrorExitCode;
        }
    }

    public static async Task<int> ExecuteAsync(
        BenchmarkCliOptions options,
        GenerationBenchmarkCatalog catalog,
        IGenerationModel model,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(model);

        var runner = new GenerationBenchmarkRunner(model, new GenerationEvaluator());
        var report = await runner.RunAsync(catalog, options.ToMetadata(catalog.Version), cancellationToken);
        var json = GenerationBenchmarkJson.Serialize(report);

        await AtomicTextFileWriter.WriteAsync(options.OutputPath, json, cancellationToken);
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
            throw new InvalidOperationException(
                $"External benchmark catalog checksum mismatch. Expected {options.CatalogSha256}, observed {actualSha256}.");
        }

        var external = ExternalGenerationBenchmarkCatalog.Parse(bytes);
        return external.ToBenchmarkCatalog();
    }
}
