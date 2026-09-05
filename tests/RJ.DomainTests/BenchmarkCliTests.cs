using RJ.Application.Benchmarking;
using RJ.Application.Generation;
using System.Text.Json;
using RJ.BenchmarkCli;
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
            var verification = BenchmarkRunManifestVerifier.Verify(Path.ChangeExtension(outputPath, ".run-manifest.json"));
            Assert.Contains("\"passed\": true", json, StringComparison.Ordinal);
            Assert.Contains("\"catalogVersion\": \"generation-benchmark-v1\"", json, StringComparison.Ordinal);
            Assert.Contains("\"modelId\": \"harness-selftest-v1\"", json, StringComparison.Ordinal);

            Assert.True(verification.Passed);
            Assert.Equal(Path.GetFullPath(outputPath), Path.GetFullPath(verification.ReportPath!));

            using var manifestJson = JsonDocument.Parse(manifest);
            var root = manifestJson.RootElement;
            Assert.Equal("benchmark-run-manifest-v1", root.GetProperty("manifestVersion").GetString());
            Assert.Equal(0, root.GetProperty("exitCode").GetInt32());
            Assert.Equal("abc123", root.GetProperty("gitCommit").GetString());
            Assert.Equal(".NET 10.0.0", root.GetProperty("runtime").GetString());
            Assert.Equal(
                Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(await File.ReadAllBytesAsync(outputPath))).ToLowerInvariant(),
                root.GetProperty("reportSha256").GetString());
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
            Assert.Equal("benchmark-run-manifest-v1", manifestJson.RootElement.GetProperty("manifestVersion").GetString());
            Assert.Equal(1, manifestJson.RootElement.GetProperty("exitCode").GetInt32());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Run_manifest_verifier_detects_report_tampering_and_missing_report()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var outputPath = Path.Combine(directory, "benchmark.json");
            var exitCode = await BenchmarkCliApp.RunAsync(
                Args(outputPath, HarnessSelfTestGenerationModelType.ModelId),
                CancellationToken.None);

            Assert.Equal(BenchmarkCliApp.SuccessExitCode, exitCode);

            var manifestPath = Path.ChangeExtension(outputPath, ".run-manifest.json");
            var validResult = BenchmarkRunManifestVerifier.Verify(manifestPath);
            Assert.True(validResult.Passed);

            await File.WriteAllTextAsync(outputPath, "tampered", CancellationToken.None);
            var tamperedResult = BenchmarkRunManifestVerifier.Verify(manifestPath);
            Assert.False(tamperedResult.Passed);
            Assert.Equal("report_checksum_mismatch", tamperedResult.ErrorCode);

            File.Delete(outputPath);
            var missingResult = BenchmarkRunManifestVerifier.Verify(manifestPath);
            Assert.False(missingResult.Passed);
            Assert.Equal("report_missing", missingResult.ErrorCode);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Run_manifest_verifier_rejects_invalid_json_missing_fields_and_checksum_and_path_escape()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var manifestPath = Path.Combine(directory, "report.run-manifest.json");
            File.WriteAllText(manifestPath, "{not-json", System.Text.Encoding.UTF8);
            var invalidJson = BenchmarkRunManifestVerifier.Verify(manifestPath);
            Assert.False(invalidJson.Passed);
            Assert.Equal("manifest_json_invalid", invalidJson.ErrorCode);

            var manifest = new
            {
                manifestVersion = "benchmark-run-manifest-v1",
                gitCommit = "abc123",
                runtime = ".NET 10.0.0",
                catalogVersion = "generation-benchmark-v1",
                modelId = "harness-selftest-v1",
                modelConfiguration = "deterministic-self-test",
                seed = "0",
                command = "cmd",
                outputPath = "report.json",
                reportSha256 = new string('a', 64),
                exitCode = 0,
                passed = true
            };
            File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest), System.Text.Encoding.UTF8);
            var missingReport = BenchmarkRunManifestVerifier.Verify(manifestPath);
            Assert.False(missingReport.Passed);
            Assert.Equal("report_missing", missingReport.ErrorCode);

            var invalidChecksumManifest = JsonSerializer.Serialize(new
            {
                manifestVersion = "benchmark-run-manifest-v1",
                gitCommit = "abc123",
                runtime = ".NET 10.0.0",
                catalogVersion = "generation-benchmark-v1",
                modelId = "harness-selftest-v1",
                modelConfiguration = "deterministic-self-test",
                seed = "0",
                command = "cmd",
                outputPath = "report.json",
                reportSha256 = "invalid",
                exitCode = 0,
                passed = true
            });
            File.WriteAllText(manifestPath, invalidChecksumManifest, System.Text.Encoding.UTF8);
            var invalidChecksum = BenchmarkRunManifestVerifier.Verify(manifestPath);
            Assert.False(invalidChecksum.Passed);
            Assert.Equal("report_checksum_invalid", invalidChecksum.ErrorCode);

            var escapeManifest = JsonSerializer.Serialize(new
            {
                manifestVersion = "benchmark-run-manifest-v1",
                gitCommit = "abc123",
                runtime = ".NET 10.0.0",
                catalogVersion = "generation-benchmark-v1",
                modelId = "harness-selftest-v1",
                modelConfiguration = "deterministic-self-test",
                seed = "0",
                command = "cmd",
                outputPath = Path.Combine("..", "outside.json"),
                reportSha256 = new string('a', 64),
                exitCode = 0,
                passed = true
            });
            File.WriteAllText(manifestPath, escapeManifest, System.Text.Encoding.UTF8);
            var escape = BenchmarkRunManifestVerifier.Verify(manifestPath);
            Assert.False(escape.Passed);
            Assert.Equal("report_path_escape", escape.ErrorCode);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Benchmark_run_manifest_cli_verifies_report_and_sanitizes_failures()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var outputPath = Path.Combine(directory, "benchmark.json");
            var exitCode = await BenchmarkCliApp.RunAsync(
                Args(outputPath, HarnessSelfTestGenerationModelType.ModelId),
                CancellationToken.None);
            Assert.Equal(BenchmarkCliApp.SuccessExitCode, exitCode);

            var manifestPath = Path.ChangeExtension(outputPath, ".run-manifest.json");
            var originalOut = Console.Out;
            using var successWriter = new StringWriter();
            Console.SetOut(successWriter);
            try
            {
                var successExit = await global::RJ.BenchmarkCli.BenchmarkRunManifestCli.RunAsync(
                    [manifestPath],
                    TextWriter.Null,
                    CancellationToken.None);
                Assert.Equal(global::RJ.BenchmarkCli.BenchmarkRunManifestCli.SuccessExitCode, successExit);
            }
            finally
            {
                Console.SetOut(originalOut);
            }
            Assert.Contains("Benchmark run manifest verified:", successWriter.ToString(), StringComparison.Ordinal);

            var invalidPathWriter = new StringWriter();
            var missingExit = await global::RJ.BenchmarkCli.BenchmarkRunManifestCli.RunAsync(
                ["missing-manifest.json"],
                invalidPathWriter,
                CancellationToken.None);
            Assert.Equal(global::RJ.BenchmarkCli.BenchmarkRunManifestCli.VerificationFailureExitCode, missingExit);
            Assert.Contains("Benchmark run manifest verification failed", invalidPathWriter.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task RunAsync_rejects_manifests_with_path_escape()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var outputPath = Path.Combine(directory, "benchmark.json");
            await BenchmarkCliApp.RunAsync(Args(outputPath, HarnessSelfTestGenerationModelType.ModelId), CancellationToken.None);

            var manifestPath = Path.ChangeExtension(outputPath, ".run-manifest.json");
            var manifest = JsonSerializer.Deserialize<Dictionary<string, object>>(await File.ReadAllTextAsync(manifestPath))!;
            manifest["outputPath"] = Path.Combine("..", "outside.json");
            await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(manifest));

            using var errorWriter = new StringWriter();
            var exit = await BenchmarkRunManifestCli.RunAsync([manifestPath], errorWriter, CancellationToken.None);

            Assert.Equal(BenchmarkRunManifestCli.VerificationFailureExitCode, exit);
            Assert.Contains("Benchmark run manifest verification failed", errorWriter.ToString(), StringComparison.Ordinal);
            Assert.Contains("report_path_escape", errorWriter.ToString(), StringComparison.Ordinal);
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
    public async Task RunAsync_selects_oab_bench_demo_target_when_explicitly_requested()
    {
        var directory = CreateTemporaryDirectory();
        var corpusRoot = Path.Combine("C:\\Projetos\\RJ", "oab-bench");
        var previous = Environment.GetEnvironmentVariable(OabBenchDemoCatalogAdapter.EnvironmentVariable);
        try
        {
            Environment.SetEnvironmentVariable(OabBenchDemoCatalogAdapter.EnvironmentVariable, corpusRoot);
            var outputPath = Path.Combine(directory, "demo-benchmark.json");
            var exitCode = await BenchmarkCliApp.RunAsync(
                [
                    "--git-commit", "abc123",
                    "--runtime", ".NET 10.0.0",
                    "--model-id", OabBenchDemoGenerationModel.ModelId,
                    "--model-config", "demo=oab-bench",
                    "--seed", "42",
                    "--output", outputPath
                ],
                CancellationToken.None);

            Assert.Equal(BenchmarkCliApp.GateFailureExitCode, exitCode);
            Assert.True(File.Exists(outputPath));
            var json = await File.ReadAllTextAsync(outputPath);
            Assert.Contains("\"catalogVersion\": \"oab-bench-demo-", json, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable(OabBenchDemoCatalogAdapter.EnvironmentVariable, previous);
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
