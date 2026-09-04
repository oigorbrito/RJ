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
}
