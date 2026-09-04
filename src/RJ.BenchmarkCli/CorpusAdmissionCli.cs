using RJ.Application.Benchmarking;

namespace RJ.BenchmarkCli;

public static class CorpusAdmissionCli
{
    public const int SuccessExitCode = 0;
    public const int AdmissionFailureExitCode = 1;
    public const int UsageOrExecutionErrorExitCode = 2;

    public static async Task<int> RunAsync(
        IReadOnlyList<string> args,
        CancellationToken cancellationToken)
    {
        try
        {
            var options = CorpusAdmissionCliOptions.Parse(args);
            var catalogBytes = await File.ReadAllBytesAsync(options.CatalogPath, cancellationToken);
            var observedCatalogSha256 = ExternalGenerationBenchmarkCatalog.ComputeSha256(catalogBytes);
            if (!StringComparer.Ordinal.Equals(observedCatalogSha256, options.CatalogSha256))
            {
                throw new InvalidOperationException(
                    $"External benchmark catalog checksum mismatch. Expected {options.CatalogSha256}, observed {observedCatalogSha256}.");
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
        catch (Exception exception)
        {
            Console.Error.WriteLine($"{exception.GetType().Name}: {exception.Message}");
            return UsageOrExecutionErrorExitCode;
        }
    }
}
