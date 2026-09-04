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

            return await ExecuteAsync(
                options,
                ApprovedGenerationBenchmarkCatalog.Create(),
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
        var report = await runner.RunAsync(catalog, options.ToMetadata(), cancellationToken);
        var json = GenerationBenchmarkJson.Serialize(report);

        await AtomicTextFileWriter.WriteAsync(options.OutputPath, json, cancellationToken);
        return report.Passed ? SuccessExitCode : GateFailureExitCode;
    }
}
