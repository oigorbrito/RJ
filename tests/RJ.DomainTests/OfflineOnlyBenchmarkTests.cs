namespace RJ.DomainTests;

public sealed class OfflineOnlyBenchmarkTests
{
    [Fact]
    public async Task BenchmarkCli_rejects_online_model_id()
    {
        using var error = new StringWriter();
        var output = Path.Combine(Path.GetTempPath(), $"rj-offline-only-{Guid.NewGuid():N}.json");

        try
        {
            var exit = await RJ.BenchmarkCli.BenchmarkCli.RunAsync(
                [
                    "--git-commit", "offline-test",
                    "--runtime", ".NET 10",
                    "--model-id", "openai-oab-rulingbr-v1",
                    "--model-config", "online",
                    "--seed", "0",
                    "--output", output
                ],
                error,
                CancellationToken.None);

            Assert.Equal(RJ.BenchmarkCli.BenchmarkCli.UsageOrExecutionErrorExitCode, exit);
            Assert.Contains("Unsupported model-id", error.ToString(), StringComparison.Ordinal);
            Assert.Contains("Supported offline ids", error.ToString(), StringComparison.Ordinal);
            Assert.False(File.Exists(output));
        }
        finally
        {
            if (File.Exists(output))
            {
                File.Delete(output);
            }

            var manifest = Path.ChangeExtension(output, ".run-manifest.json");
            if (File.Exists(manifest))
            {
                File.Delete(manifest);
            }
        }
    }
}
