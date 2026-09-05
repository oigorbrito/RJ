using RJ.Application.Benchmarking;

namespace RJ.BenchmarkCli;

public static class CorpusAdmissionCli
{
    public const int SuccessExitCode = 0;
    public const int AdmissionFailureExitCode = 1;
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
            if (args.Count == 0)
            {
                return await RunDemoAsync(cancellationToken);
            }

            var options = CorpusAdmissionCliOptions.Parse(args);
            var catalogBytes = await File.ReadAllBytesAsync(options.CatalogPath, cancellationToken);
            var observedCatalogSha256 = ExternalGenerationBenchmarkCatalog.ComputeSha256(catalogBytes);
            if (!StringComparer.Ordinal.Equals(observedCatalogSha256, options.CatalogSha256))
            {
                throw new InvalidOperationException("External benchmark catalog checksum mismatch.");
            }

            var externalCatalog = ExternalGenerationBenchmarkCatalog.Parse(catalogBytes);
            var service = new CorpusAdmissionService(new LocalBenchmarkArtifactReader(options.CatalogPath));
            var report = await service.AdmitAsync(externalCatalog, observedCatalogSha256, cancellationToken);
            var json = CorpusAdmissionJson.Serialize(report);

            await AtomicTextFileWriter.WriteAsync(options.OutputPath, json, cancellationToken);
            return report.Passed ? SuccessExitCode : AdmissionFailureExitCode;
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
            await errorWriter.WriteLineAsync($"{exception.GetType().Name}: Corpus admission execution failed.");
            return UsageOrExecutionErrorExitCode;
        }
    }

    private static async Task<int> RunDemoAsync(CancellationToken cancellationToken)
    {
        var corpusRoot = Environment.GetEnvironmentVariable(OabBenchDemoCatalogAdapter.EnvironmentVariable);
        if (string.IsNullOrWhiteSpace(corpusRoot))
        {
            throw new InvalidOperationException($"Environment variable {OabBenchDemoCatalogAdapter.EnvironmentVariable} is required for demo corpus admission.");
        }

        var adapter = await OabBenchDemoCatalogAdapter.BuildAsync(corpusRoot, null, cancellationToken);
        var catalogBytes = await File.ReadAllBytesAsync(adapter.CatalogPath, cancellationToken);
        var externalCatalog = ExternalGenerationBenchmarkCatalog.Parse(catalogBytes);
        var service = new CorpusAdmissionService(new LocalBenchmarkArtifactReader(adapter.CatalogPath));
        var report = await service.AdmitAsync(externalCatalog, adapter.CatalogSha256, cancellationToken);
        var outputPath = Path.Combine(Path.GetTempPath(), "rj-oab-demo-admission.json");
        await AtomicTextFileWriter.WriteAsync(outputPath, CorpusAdmissionJson.Serialize(report), cancellationToken);
        return report.Passed ? SuccessExitCode : AdmissionFailureExitCode;
    }
}
