using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Npgsql;
using RJ.Application.Benchmarking;
using RJ.Infrastructure.Persistence;

if (args.Length != 7)
{
    Console.Error.WriteLine("Usage: RJ.RetrievalBenchmarkRunner <catalog.json> <expected-catalog-sha256> <treatment-id> <config.json> <expected-config-sha256> <config-reference> <output-report.json>");
    return 2;
}

var catalogPath = Path.GetFullPath(args[0]);
var expectedCatalogSha = args[1].Trim().ToLowerInvariant();
var treatmentId = args[2].Trim();
var configPath = Path.GetFullPath(args[3]);
var expectedConfigSha = args[4].Trim().ToLowerInvariant();
var configReference = args[5].Trim();
var outputPath = Path.GetFullPath(args[6]);
var connectionString = Environment.GetEnvironmentVariable("RJ_POSTGRES_CONNECTION");

if (!File.Exists(catalogPath) || !File.Exists(configPath) || string.IsNullOrWhiteSpace(connectionString))
{
    Console.Error.WriteLine("Retrieval catalog/configuration or RJ_POSTGRES_CONNECTION is unavailable.");
    return 2;
}

try
{
    var catalogBytes = await File.ReadAllBytesAsync(catalogPath);
    VerifySha(catalogBytes, expectedCatalogSha, "retrieval catalog");
    var catalog = RetrievalBenchmarkCatalogJson.Parse(catalogBytes);

    var configBytes = await File.ReadAllBytesAsync(configPath);
    VerifySha(configBytes, expectedConfigSha, "retrieval configuration");
    var config = JsonSerializer.Deserialize<RetrievalBenchmarkExecutionConfiguration>(
        configBytes,
        new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
        ?? throw new InvalidOperationException("Retrieval benchmark configuration produced no document.");
    config.Validate();

    await using var dataSource = NpgsqlDataSource.Create(connectionString);
    await PostgresSchema.EnsureCurrentAsync(dataSource);
    var search = new PostgresLegalDocumentSearch(dataSource);
    if (!StringComparer.Ordinal.Equals(config.ImplementationId, search.ImplementationId))
    {
        throw new InvalidOperationException(
            $"Configured retrieval implementation '{config.ImplementationId}' does not match runtime implementation '{search.ImplementationId}'.");
    }

    var gitHead = await ResolveGitHeadAsync();
    var execution = new RetrievalBenchmarkExecutionMetadata(gitHead, RuntimeInformation.FrameworkDescription);
    var treatment = new RetrievalBenchmarkTreatmentMetadata(
        treatmentId,
        search.ImplementationId,
        configReference,
        expectedConfigSha);
    var report = await new RetrievalBenchmarkRunner(search).RunAsync(
        catalog,
        execution,
        treatment,
        config.Limit,
        CancellationToken.None);

    var json = RetrievalBenchmarkJson.Serialize(report);
    var outputDirectory = Path.GetDirectoryName(outputPath);
    if (!string.IsNullOrWhiteSpace(outputDirectory))
    {
        Directory.CreateDirectory(outputDirectory);
    }
    await File.WriteAllTextAsync(outputPath, json + Environment.NewLine);
    var outputBytes = await File.ReadAllBytesAsync(outputPath);
    Console.WriteLine(JsonSerializer.Serialize(new
    {
        treatmentId,
        implementationId = search.ImplementationId,
        gitCommit = gitHead,
        runtime = RuntimeInformation.FrameworkDescription,
        catalogVersion = catalog.Version,
        caseCount = report.Cases.Count,
        outputPath,
        outputSha256 = EmpiricalSelectionManifest.ComputeSha256(outputBytes)
    }));
    return 0;
}
catch (JsonException exception)
{
    Console.Error.WriteLine(exception.Message);
    return 3;
}
catch (InvalidOperationException exception)
{
    Console.Error.WriteLine(exception.Message);
    return 3;
}
catch (ArgumentException exception)
{
    Console.Error.WriteLine(exception.Message);
    return 3;
}
catch (IOException exception)
{
    Console.Error.WriteLine(exception.Message);
    return 3;
}
catch (UnauthorizedAccessException exception)
{
    Console.Error.WriteLine(exception.Message);
    return 3;
}
catch (NpgsqlException exception)
{
    Console.Error.WriteLine($"PostgreSQL benchmark execution failed: {exception.GetType().Name}.");
    return 4;
}

static void VerifySha(ReadOnlySpan<byte> bytes, string expected, string label)
{
    var actual = EmpiricalSelectionManifest.ComputeSha256(bytes);
    if (!StringComparer.Ordinal.Equals(actual, expected))
    {
        throw new InvalidOperationException($"{label} SHA-256 mismatch: expected {expected}, observed {actual}.");
    }
}

static async Task<string> ResolveGitHeadAsync()
{
    using var process = new Process
    {
        StartInfo = new ProcessStartInfo
        {
            FileName = "git",
            Arguments = "rev-parse HEAD",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        }
    };

    if (!process.Start())
    {
        throw new InvalidOperationException("Unable to start git to resolve the executed commit.");
    }

    var output = await process.StandardOutput.ReadToEndAsync();
    var error = await process.StandardError.ReadToEndAsync();
    await process.WaitForExitAsync();
    if (process.ExitCode != 0)
    {
        throw new InvalidOperationException($"Unable to resolve executed git commit: {error.Trim()}.");
    }

    var head = output.Trim().ToLowerInvariant();
    _ = new RetrievalBenchmarkExecutionMetadata(head, RuntimeInformation.FrameworkDescription);
    return head;
}

internal sealed record RetrievalBenchmarkExecutionConfiguration(
    string FormatVersion,
    string ImplementationId,
    int Limit)
{
    public const string SupportedFormatVersion = "rjudi-retrieval-benchmark-config-v1";

    public void Validate()
    {
        if (!StringComparer.Ordinal.Equals(FormatVersion, SupportedFormatVersion))
        {
            throw new InvalidOperationException($"Unsupported retrieval benchmark configuration format '{FormatVersion}'.");
        }

        if (string.IsNullOrWhiteSpace(ImplementationId))
        {
            throw new InvalidOperationException("Retrieval implementation id is required.");
        }

        if (Limit is < 5 or > 100)
        {
            throw new InvalidOperationException("Retrieval benchmark limit must be between 5 and 100.");
        }
    }
}
