using RJ.Application.Benchmarking;
using RJ.Application.Generation;
using RJ.BenchmarkCli;

namespace RJ.DomainTests;

public sealed class BenchmarkCliTests
{
    [Fact]
    public async Task RunAsync_self_test_writes_passing_report_and_returns_zero()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var outputPath = Path.Combine(directory, "benchmark.json");
            var exitCode = await BenchmarkCli.RunAsync(
                Args(outputPath, HarnessSelfTestGenerationModel.ModelId),
                CancellationToken.None);

            Assert.Equal(BenchmarkCli.SuccessExitCode, exitCode);
            var json = await File.ReadAllTextAsync(outputPath);
            Assert.Contains("\"passed\": true", json, StringComparison.Ordinal);
            Assert.Contains("\"catalogVersion\": \"generation-benchmark-v1\"", json, StringComparison.Ordinal);
            Assert.Contains("\"modelId\": \"harness-selftest-v1\"", json, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task ExecuteAsync_writes_failed_report_before_returning_gate_failure_exit_code()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var outputPath = Path.Combine(directory, "failed.json");
            var options = BenchmarkCliOptions.Parse(Args(outputPath, HarnessSelfTestGenerationModel.ModelId));

            var exitCode = await BenchmarkCli.ExecuteAsync(
                options,
                ApprovedGenerationBenchmarkCatalog.Create(),
                new InvalidCitationModel(),
                CancellationToken.None);

            Assert.Equal(BenchmarkCli.GateFailureExitCode, exitCode);
            var json = await File.ReadAllTextAsync(outputPath);
            Assert.Contains("\"passed\": false", json, StringComparison.Ordinal);
            Assert.Contains("\"failedCases\":", json, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task RunAsync_rejects_unavailable_model_before_creating_report()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var outputPath = Path.Combine(directory, "benchmark.json");
            var exitCode = await BenchmarkCli.RunAsync(
                Args(outputPath, "not-available"),
                CancellationToken.None);

            Assert.Equal(BenchmarkCli.UsageOrExecutionErrorExitCode, exitCode);
            Assert.False(File.Exists(outputPath));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Atomic_writer_replaces_target_and_leaves_no_temporary_file()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var outputPath = Path.Combine(directory, "report.json");
            await AtomicTextFileWriter.WriteAsync(outputPath, "first", CancellationToken.None);
            await AtomicTextFileWriter.WriteAsync(outputPath, "second", CancellationToken.None);

            Assert.Equal("second", await File.ReadAllTextAsync(outputPath));
            Assert.Equal(new[] { outputPath }, Directory.GetFiles(directory));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Options_parser_rejects_duplicate_argument_names()
    {
        var args = Args("report.json", HarnessSelfTestGenerationModel.ModelId).ToList();
        args.Add("--seed");
        args.Add("duplicate");

        Assert.Throws<ArgumentException>(() => BenchmarkCliOptions.Parse(args));
    }

    private static string[] Args(string outputPath, string modelId) =>
    [
        "--git-commit", "abc123",
        "--runtime", ".NET 10.0.0",
        "--model-id", modelId,
        "--model-config", "temperature=0",
        "--seed", "42",
        "--output", outputPath
    ];

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"rj-benchmark-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class InvalidCitationModel : IGenerationModel
    {
        public Task<GenerationModelOutput> GenerateAsync(
            GenerationContext context,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            return Task.FromResult(new GenerationModelOutput(
                false,
                null,
                [new GenerationClaim(
                    context.Query,
                    [new GenerationCitation("doc-invalid", new string('f', 64), 0, 1)])]));
        }
    }
}
