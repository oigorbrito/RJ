namespace RJ.BenchmarkCli;

public static class BenchmarkRunManifestCli
{
    public const int SuccessExitCode = 0;
    public const int VerificationFailureExitCode = 2;

    public static Task<int> RunAsync(IReadOnlyList<string> args, CancellationToken cancellationToken) =>
        RunAsync(args, Console.Error, cancellationToken);

    public static async Task<int> RunAsync(
        IReadOnlyList<string> args,
        TextWriter errorWriter,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(errorWriter);

        try
        {
            if (args.Count != 1 || string.IsNullOrWhiteSpace(args[0]))
            {
                throw new ArgumentException("Required argument is missing or empty: <manifest-path>", nameof(args));
            }

            cancellationToken.ThrowIfCancellationRequested();
            var result = BenchmarkRunManifestVerifier.Verify(args[0]);
            if (result.Passed)
            {
                await Console.Out.WriteLineAsync($"Benchmark run manifest verified: {result.ReportPath}");
                return SuccessExitCode;
            }

            await errorWriter.WriteLineAsync($"InvalidOperationException: Benchmark run manifest verification failed ({result.ErrorCode}).");
            return VerificationFailureExitCode;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (ArgumentException exception)
        {
            await errorWriter.WriteLineAsync($"{exception.GetType().Name}: {exception.Message}");
            return VerificationFailureExitCode;
        }
        catch (Exception)
        {
            await errorWriter.WriteLineAsync("InvalidOperationException: Benchmark run manifest verification failed.");
            return VerificationFailureExitCode;
        }
    }
}
