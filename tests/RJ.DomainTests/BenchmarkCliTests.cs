using RJ.Application.Benchmarking;
using RJ.Application.Generation;
using System.Text.Json;
using ApprovedGenerationBenchmarkCatalogType = global::RJ.BenchmarkCli.ApprovedGenerationBenchmarkCatalog;
using AtomicTextFileWriterType = global::RJ.BenchmarkCli.AtomicTextFileWriter;
using BenchmarkCliApp = global::RJ.BenchmarkCli.BenchmarkCli;
using BenchmarkCliOptionsType = global::RJ.BenchmarkCli.BenchmarkCliOptions;
using HarnessSelfTestGenerationModelType = global::RJ.BenchmarkCli.HarnessSelfTestGenerationModel;

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
            var exitCode = await BenchmarkCliApp.RunAsync(
                Args(outputPath, HarnessSelfTestGenerationModelType.ModelId),
                CancellationToken.None);

            Assert.Equal(BenchmarkCliApp.SuccessExitCode, exitCode);
            var json = await File.ReadAllTextAsync(outputPath);
            var manifest = await File.ReadAllTextAsync(Path.ChangeExtension(outputPath, ".run-manifest.json"));
            Assert.Contains("\"passed\": true", json, StringComparison.Ordinal);
            Assert.Contains("\"catalogVersion\": \"generation-benchmark-v1\"", json, StringComparison.Ordinal);
            Assert.Contains("\"modelId\": \"harness-selftest-v1\"", json, StringComparison.Ordinal);

            using var manifestJson = JsonDocument.Parse(manifest);
            var root = manifestJson.RootElement;
            Assert.Equal(0, root.GetProperty("exitCode").GetInt32());
            Assert.Equal("abc123", root.GetProperty("gitCommit").GetString());
            Assert.Equal(".NET 10.0.0", root.GetProperty("runtime").GetString());
            Assert.Contains("--git-commit abc123", root.GetProperty("command").GetString(), StringComparison.Ordinal);
            Assert.Contains(Path.GetFullPath(outputPath), root.GetProperty("command").GetString(), StringComparison.Ordinal);
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
            var options = BenchmarkCliOptionsType.Parse(Args(outputPath, HarnessSelfTestGenerationModelType.ModelId));

            var exitCode = await BenchmarkCliApp.ExecuteAsync(
                options,
                ApprovedGenerationBenchmarkCatalogType.Create(),
                new InvalidCitationModel(),
                "--git-commit abc123 --runtime .NET 10.0.0 --model-id harness-selftest-v1 --model-config temperature=0 --seed 42 --output failed.json",
                CancellationToken.None);

            Assert.Equal(BenchmarkCliApp.GateFailureExitCode, exitCode);
            var json = await File.ReadAllTextAsync(outputPath);
            var manifest = await File.ReadAllTextAsync(Path.ChangeExtension(outputPath, ".run-manifest.json"));
            Assert.Contains("\"passed\": false", json, StringComparison.Ordinal);
            Assert.Contains("\"failedCases\":", json, StringComparison.Ordinal);

            using var manifestJson = JsonDocument.Parse(manifest);
            Assert.Equal(1, manifestJson.RootElement.GetProperty("exitCode").GetInt32());
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
            var exitCode = await BenchmarkCliApp.RunAsync(
                Args(outputPath, "not-available"),
                CancellationToken.None);

            Assert.Equal(BenchmarkCliApp.UsageOrExecutionErrorExitCode, exitCode);
            Assert.False(File.Exists(outputPath));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task RunAsync_rejects_external_catalog_checksum_mismatch_before_creating_report()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var catalogPath = Path.Combine(directory, "catalog.json");
            var outputPath = Path.Combine(directory, "benchmark.json");
            await File.WriteAllTextAsync(catalogPath, "{}");
            var args = Args(outputPath, HarnessSelfTestGenerationModelType.ModelId).ToList();
            args.Add("--catalog");
            args.Add(catalogPath);
            args.Add("--catalog-sha256");
            args.Add(new string('a', 64));
            using var errorWriter = new StringWriter();

            var exitCode = await BenchmarkCliApp.RunAsync(args, errorWriter, CancellationToken.None);
            var diagnostic = errorWriter.ToString();

            Assert.Equal(BenchmarkCliApp.UsageOrExecutionErrorExitCode, exitCode);
            Assert.False(File.Exists(outputPath));
            Assert.Contains("InvalidOperationException: Benchmark execution failed.", diagnostic, StringComparison.Ordinal);
            Assert.DoesNotContain(catalogPath, diagnostic, StringComparison.Ordinal);
            Assert.DoesNotContain(new string('a', 64), diagnostic, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task RunAsync_does_not_expose_missing_catalog_path_in_execution_diagnostic()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var catalogPath = Path.Combine(directory, "secret-catalog-name.json");
            var outputPath = Path.Combine(directory, "benchmark.json");
            var args = Args(outputPath, HarnessSelfTestGenerationModelType.ModelId).ToList();
            args.Add("--catalog");
            args.Add(catalogPath);
            args.Add("--catalog-sha256");
            args.Add(new string('b', 64));
            using var errorWriter = new StringWriter();

            var exitCode = await BenchmarkCliApp.RunAsync(args, errorWriter, CancellationToken.None);
            var diagnostic = errorWriter.ToString();

            Assert.Equal(BenchmarkCliApp.UsageOrExecutionErrorExitCode, exitCode);
            Assert.Contains("FileNotFoundException: Benchmark execution failed.", diagnostic, StringComparison.Ordinal);
            Assert.DoesNotContain(catalogPath, diagnostic, StringComparison.Ordinal);
            Assert.DoesNotContain("secret-catalog-name.json", diagnostic, StringComparison.Ordinal);
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
            await AtomicTextFileWriterType.WriteAsync(outputPath, "first", CancellationToken.None);
            await AtomicTextFileWriterType.WriteAsync(outputPath, "second", CancellationToken.None);

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
        var args = Args("report.json", HarnessSelfTestGenerationModelType.ModelId).ToList();
        args.Add("--seed");
        args.Add("duplicate");

        Assert.Throws<ArgumentException>(() => BenchmarkCliOptionsType.Parse(args));
    }

    [Fact]
    public void Options_parser_requires_external_catalog_path_and_checksum_together()
    {
        var args = Args("report.json", HarnessSelfTestGenerationModelType.ModelId).ToList();
        args.Add("--catalog");
        args.Add("catalog.json");

        Assert.Throws<ArgumentException>(() => BenchmarkCliOptionsType.Parse(args));
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
