using System.Text;
using RJ.Application.Benchmarking;

namespace RJ.DomainTests;

public sealed class CorpusAdmissionServiceTests
{
    [Fact]
    public async Task AdmitAsync_passes_when_source_oracle_hashes_and_excerpt_are_reproducible()
    {
        var fixture = Fixture.Create();
        var service = new CorpusAdmissionService(new DictionaryArtifactReader(fixture.Artifacts));

        var report = await service.AdmitAsync(fixture.Catalog, fixture.CatalogSha256, CancellationToken.None);

        Assert.True(report.Passed);
        Assert.Equal(1, report.TotalCases);
        Assert.Equal(1, report.TotalEvidenceItems);
        Assert.Equal(1, report.VerifiedEvidenceItems);
        Assert.Equal(1, report.VerifiedOracles);
        Assert.True(Assert.Single(report.Cases).Passed);
    }

    [Fact]
    public async Task AdmitAsync_fails_when_source_hash_does_not_match_resolved_bytes()
    {
        var fixture = Fixture.Create(sourceHashOverride: new string('f', 64));
        var service = new CorpusAdmissionService(new DictionaryArtifactReader(fixture.Artifacts));

        var report = await service.AdmitAsync(fixture.Catalog, fixture.CatalogSha256, CancellationToken.None);

        Assert.False(report.Passed);
        var failure = Assert.Single(Assert.Single(report.Cases).Failures);
        Assert.Equal("source-sha256", failure.Gate);
        Assert.Contains(new string('f', 64), failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AdmitAsync_fails_when_source_is_not_strict_utf8_even_with_matching_hash()
    {
        var fixture = Fixture.Create(sourceBytesOverride: [0xff, 0xfe, 0xfd]);
        var service = new CorpusAdmissionService(new DictionaryArtifactReader(fixture.Artifacts));

        var report = await service.AdmitAsync(fixture.Catalog, fixture.CatalogSha256, CancellationToken.None);

        Assert.False(report.Passed);
        var failure = Assert.Single(Assert.Single(report.Cases).Failures);
        Assert.Equal("source-utf8", failure.Gate);
        Assert.Equal("Resolved source artifact is not valid strict UTF-8.", failure.Message);
    }

    [Fact]
    public async Task AdmitAsync_fails_when_catalog_excerpt_cannot_be_reproduced_from_source_offsets()
    {
        var fixture = Fixture.Create(excerptOverride: "indeferi");
        var service = new CorpusAdmissionService(new DictionaryArtifactReader(fixture.Artifacts));

        var report = await service.AdmitAsync(fixture.Catalog, fixture.CatalogSha256, CancellationToken.None);

        Assert.False(report.Passed);
        var failure = Assert.Single(Assert.Single(report.Cases).Failures);
        Assert.Equal("excerpt-reproduction", failure.Gate);
    }

    [Fact]
    public async Task AdmitAsync_fails_when_oracle_hash_does_not_match_resolved_bytes()
    {
        var fixture = Fixture.Create(oracleHashOverride: new string('e', 64));
        var service = new CorpusAdmissionService(new DictionaryArtifactReader(fixture.Artifacts));

        var report = await service.AdmitAsync(fixture.Catalog, fixture.CatalogSha256, CancellationToken.None);

        Assert.False(report.Passed);
        Assert.False(Assert.Single(report.Cases).OracleVerified);
        var failure = Assert.Single(Assert.Single(report.Cases).Failures);
        Assert.Equal("oracle-sha256", failure.Gate);
        Assert.Contains(new string('e', 64), failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AdmitAsync_sanitizes_reader_exception_messages_in_persistable_failures()
    {
        var fixture = Fixture.Create();
        const string sentinelPath = "/sensitive/internal/root/oracle/fixture-001.txt";
        var service = new CorpusAdmissionService(new ThrowingArtifactReader(sentinelPath));

        var report = await service.AdmitAsync(fixture.Catalog, fixture.CatalogSha256, CancellationToken.None);

        Assert.False(report.Passed);
        var failures = Assert.Single(report.Cases).Failures;
        Assert.NotEmpty(failures);
        Assert.All(failures, failure => Assert.DoesNotContain(sentinelPath, failure.Message, StringComparison.Ordinal));
        Assert.Contains(failures, failure => failure.Message.Contains("could not be read", StringComparison.Ordinal));
    }

    private sealed class DictionaryArtifactReader(IReadOnlyDictionary<string, byte[]> artifacts) : IBenchmarkArtifactReader
    {
        public Task<ReadOnlyMemory<byte>> ReadAsync(string artifactReference, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return artifacts.TryGetValue(artifactReference, out var bytes)
                ? Task.FromResult<ReadOnlyMemory<byte>>(bytes)
                : throw new FileNotFoundException("Artifact not found.", artifactReference);
        }
    }

    private sealed class ThrowingArtifactReader(string sentinelPath) : IBenchmarkArtifactReader
    {
        public Task<ReadOnlyMemory<byte>> ReadAsync(string artifactReference, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw new FileNotFoundException($"Could not find file '{sentinelPath}'.", sentinelPath);
        }
    }

    private sealed record Fixture(
        ExternalGenerationBenchmarkCatalog Catalog,
        string CatalogSha256,
        IReadOnlyDictionary<string, byte[]> Artifacts)
    {
        public static Fixture Create(
            string? sourceHashOverride = null,
            string? oracleHashOverride = null,
            string? excerptOverride = null,
            byte[]? sourceBytesOverride = null)
        {
            var sourceBytes = sourceBytesOverride ?? Encoding.UTF8.GetBytes("deferido");
            var oracleBytes = Encoding.UTF8.GetBytes("oracle-v1");
            var actualSourceHash = ExternalGenerationBenchmarkCatalog.ComputeSha256(sourceBytes);
            var actualOracleHash = ExternalGenerationBenchmarkCatalog.ComputeSha256(oracleBytes);
            var sourceHash = sourceHashOverride ?? actualSourceHash;
            var oracleHash = oracleHashOverride ?? actualOracleHash;
            var excerpt = excerptOverride ?? "deferido";

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
                        excerpt,
                        0,
                        excerpt.Length,
                        8,
                        1.0f)],
                    [new ExternalExpectedGenerationClaim(
                        "O pedido foi deferido.",
                        [new ExternalGenerationCitation("doc-1", sourceHash, 0, excerpt.Length)])])]);

            return new Fixture(
                catalog,
                new string('c', 64),
                new Dictionary<string, byte[]>(StringComparer.Ordinal)
                {
                    ["sources/doc-1.txt"] = sourceBytes,
                    ["oracle/fixture-001.txt"] = oracleBytes
                });
        }
    }
}
