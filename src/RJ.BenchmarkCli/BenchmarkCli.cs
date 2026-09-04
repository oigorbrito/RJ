using RJ.Application.Benchmarking;
using RJ.Application.Evaluation;

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

            var catalog = ApprovedGenerationBenchmarkCatalog.Create();
            var model = new HarnessSelfTestGenerationModel();
            var runner = new GenerationBenchmarkRunner(model, new GenerationEvaluator());
            var report = await runner.RunAsync(catalog, options.ToMetadata(), cancellationToken);
            var json = GenerationBenchmarkJson.Serialize(report);

            await AtomicTextFileWriter.WriteAsync(options.OutputPath, json, cancellationToken);
            return report.Passed ? SuccessExitCode : GateFailureExitCode;
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
}
