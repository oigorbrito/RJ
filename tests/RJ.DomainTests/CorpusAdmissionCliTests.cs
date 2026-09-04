using System.Text;
using System.Text.Json;
using RJ.Application.Benchmarking;
using RJ.BenchmarkCli;

namespace RJ.DomainTests;

public sealed class CorpusAdmissionCliTests
{
    [Fact]
    public async Task RunAsync_valid_catalog_and_artifacts_writes_passing_admission_report()
    {
        var fixture = await LocalFixture.CreateAsync();
        try
        {
            var output = Path.Combine(fixture.Root, "admission.json");
            var exitCode = await CorpusAdmissionCli.RunAsync(
                Args(fixture.CatalogPath, fixture.CatalogSha256, output),
                CancellationToken.None);

            Assert.Equal(CorpusAdmissionCli.SuccessExitCode, exitCode);
            var json = await File.ReadAllTextAsync(output);
            Assert.Contains("\"passed\": true", json, StringComparison.Ordinal);
            Assert.Contains("\"verifiedEvidenceItems\": 1", json, StringComparison.Ordinal);
            Assert.Contains("\"verifiedOracles\": 1", json, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(fixture.Root, recursive: true);
        }
    }

    [Fact]
    public async Task RunAsync_catalog_checksum_mismatch_returns_execution_error_without_report()
    {
        var fixture = await LocalFixture.CreateAsync();
        try
        {
            var output = Path.Combine(fixture.Root, "admission.json");
            var exitCode = await CorpusAdmissionCli.RunAsync(
                Args(fixture.CatalogPath, new string('f', 64), output),
                CancellationToken.None);

            Assert.Equal(CorpusAdmissionCli.UsageOrExecutionErrorExitCode, exitCode);
            Assert.False(File.Exists(output));
        }
        finally
        {
            Directory.Delete(fixture.Root, recursive: true);
        }
    }

    [Fact]
    public async Task Local_reader_rejects_reference_that_escapes_catalog_directory()
    {
        var fixture = await LocalFixture.CreateAsync();
        try
        {
            var reader = new LocalBenchmarkArtifactReader(fixture.CatalogPath);

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                reader.ReadAsync("../outside.txt", CancellationToken.None));
        }
        finally
        {
            Directory.Delete(fixture.Root, recursive: true);
        }
    }

    private static string[] Args(string catalog, string checksum, string output) =>
    [
        "--catalog", catalog,
        "--catalog-sha256", checksum,
        "--output", output
    ];

    private sealed record LocalFixture(string Root, string CatalogPath, string CatalogSha256)
    {
        public static async Task<LocalFixture> CreateAsync()
        {
            var root = Path.Combine(Path.GetTempPath(), $"rj-corpus-admission-{Guid.NewGuid():N}");
            Directory.CreateDirectory(root);
            Directory.CreateDirectory(Path.Combine(root, "sources"));
            Directory.CreateDirectory(Path.Combine(root, "oracle"));

            var sourceBytes = Encoding.UTF8.GetBytes("deferido");
            var oracleBytes = Encoding.UTF8.GetBytes("oracle-v1");
            var sourceHash = ExternalGenerationBenchmarkCatalog.ComputeSha256(sourceBytes);
            var oracleHash = ExternalGenerationBenchmarkCatalog.ComputeSha256(oracleBytes);

            await File.WriteAllBytesAsync(Path.Combine(root, "sources", "doc-1.txt"), sourceBytes);
            await File.WriteAllBytesAsync(Path.Combine(root, "oracle", "fixture-001.txt"), oracleBytes);

            var catalog = new ExternalGenerationBenchmarkCatalog(
                ExternalGenerationBenchmarkCatalog.SupportedFormatVersion,
                "legal-corpus-v1",
                [new ExternalGenerationBenchmarkCase(
                    "fixture-001",
                    "case-1",
                    "Qual foi a decisão?",
                    "oracle/fixture-001.txt",
                    oracleHash,
                    false,
                    [new ExternalGenerationContextItem(
                        "doc-1",
                        "decisao.txt",
                        "sources/doc-1.txt",
                        sourceHash,
                        sourceHash,
                        "deferido",
                        0,
                        8,
                        8,
                        1.0f)],
                    [new ExternalExpectedGenerationClaim(
                        "O pedido foi deferido.",
                        [new ExternalGenerationCitation("doc-1", sourceHash, 0, 8)])])]);

            var catalogBytes = JsonSerializer.SerializeToUtf8Bytes(catalog);
            var catalogPath = Path.Combine(root, "catalog.json");
            await File.WriteAllBytesAsync(catalogPath, catalogBytes);
            var catalogSha256 = ExternalGenerationBenchmarkCatalog.ComputeSha256(catalogBytes);
            return new LocalFixture(root, catalogPath, catalogSha256);
        }
    }
}
