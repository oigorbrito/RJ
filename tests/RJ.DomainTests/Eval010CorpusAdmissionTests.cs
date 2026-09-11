using System.Text;
using RJ.Application.Benchmarking;

namespace RJ.DomainTests;

public sealed class Eval010CorpusAdmissionTests
{
    [Fact]
    public void Manifest_requires_pre_specified_30_to_50_case_range()
    {
        var manifest = Manifest(29);

        var error = Assert.Throws<InvalidOperationException>(manifest.Validate);

        Assert.Contains("between 30 and 50", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Manifest_requires_independent_oracle_reviewer_identifier()
    {
        var manifest = Manifest(30);
        var first = manifest.Cases[0];
        manifest = manifest with
        {
            Cases = [first with { OracleReviewerId = first.OracleAuthorId }, .. manifest.Cases.Skip(1)]
        };

        var error = Assert.Throws<InvalidOperationException>(manifest.Validate);

        Assert.Contains("author and reviewer", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Admission_verifies_source_oracle_and_independent_review_hashes_for_all_cases()
    {
        var artifacts = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        var cases = Enumerable.Range(1, 30)
            .Select(index => Case(index, artifacts))
            .ToArray();
        var manifest = new Eval010CorpusManifest(
            Eval010CorpusManifest.SupportedFormatVersion,
            "eval010-test-corpus-v1",
            DateTimeOffset.Parse("2026-09-11T00:00:00Z", System.Globalization.CultureInfo.InvariantCulture),
            cases);
        var service = new Eval010CorpusAdmissionService(new DictionaryArtifactReader(artifacts));

        var report = await service.AdmitAsync(manifest, null, CancellationToken.None);

        Assert.True(report.Passed);
        Assert.Equal(30, report.CaseCount);
        Assert.All(report.Cases, item =>
        {
            Assert.True(item.SourceVerified);
            Assert.True(item.OracleVerified);
            Assert.True(item.OracleReviewVerified);
            Assert.True(item.Passed);
            Assert.Empty(item.Failures);
        });
    }

    [Fact]
    public void Benchmark_evidence_cannot_reference_any_oracle_or_review_artifact()
    {
        var artifacts = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        var cases = Enumerable.Range(1, 30)
            .Select(index => Case(index, artifacts))
            .ToArray();
        var manifest = new Eval010CorpusManifest(
            Eval010CorpusManifest.SupportedFormatVersion,
            "eval010-test-corpus-v1",
            DateTimeOffset.Parse("2026-09-11T00:00:00Z", System.Globalization.CultureInfo.InvariantCulture),
            cases);
        var catalogCases = cases.Select((item, index) => CatalogCase(item, index == 0 ? item.OracleReference : item.SourceReference)).ToArray();
        var catalog = new ExternalGenerationBenchmarkCatalog(
            ExternalGenerationBenchmarkCatalog.SupportedFormatVersion,
            "eval010-test-benchmark-v1",
            catalogCases);

        var error = Assert.Throws<InvalidOperationException>(
            () => Eval010CorpusAdmissionService.ValidateOracleIsolation(manifest, catalog));

        Assert.Contains("Oracle isolation violation", error.Message, StringComparison.Ordinal);
    }

    private static Eval010CorpusManifest Manifest(int count)
    {
        var artifacts = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        return new Eval010CorpusManifest(
            Eval010CorpusManifest.SupportedFormatVersion,
            "eval010-test-corpus-v1",
            DateTimeOffset.Parse("2026-09-11T00:00:00Z", System.Globalization.CultureInfo.InvariantCulture),
            Enumerable.Range(1, count).Select(index => Case(index, artifacts)).ToArray());
    }

    private static Eval010CorpusCase Case(int index, IDictionary<string, byte[]> artifacts)
    {
        var sourceReference = $"sources/case-{index:D2}.json";
        var oracleReference = $"oracles/case-{index:D2}.json";
        var reviewReference = $"oracle-reviews/case-{index:D2}.json";
        var source = Encoding.UTF8.GetBytes($"source-{index:D2}");
        var oracle = Encoding.UTF8.GetBytes($"oracle-{index:D2}");
        var review = Encoding.UTF8.GetBytes($"review-{index:D2}");
        artifacts[sourceReference] = source;
        artifacts[oracleReference] = oracle;
        artifacts[reviewReference] = review;

        return new Eval010CorpusCase(
            $"case-{index:D2}",
            $"0000000-{index % 90:D2}.2026.8.16.{index:D4}",
            sourceReference,
            ExternalGenerationBenchmarkCatalog.ComputeSha256(source),
            oracleReference,
            ExternalGenerationBenchmarkCatalog.ComputeSha256(oracle),
            reviewReference,
            ExternalGenerationBenchmarkCatalog.ComputeSha256(review),
            $"oracle-author-{index:D2}",
            $"oracle-reviewer-{index:D2}",
            DateTimeOffset.Parse("2026-09-10T00:00:00Z", System.Globalization.CultureInfo.InvariantCulture));
    }

    private static ExternalGenerationBenchmarkCase CatalogCase(Eval010CorpusCase item, string evidenceReference)
    {
        const string excerpt = "claim";
        var contentSha = ExternalGenerationBenchmarkCatalog.ComputeSha256(excerpt);
        return new ExternalGenerationBenchmarkCase(
            item.CaseId,
            item.CaseId,
            "summarize",
            item.OracleReference,
            item.OracleSha256,
            false,
            [new ExternalGenerationContextItem(
                $"doc-{item.CaseId}",
                "test-source",
                evidenceReference,
                contentSha,
                contentSha,
                excerpt,
                0,
                excerpt.Length,
                excerpt.Length,
                1.0f)],
            [new ExternalExpectedGenerationClaim(
                "claim",
                [new ExternalGenerationCitation($"doc-{item.CaseId}", contentSha, 0, excerpt.Length)])]);
    }

    private sealed class DictionaryArtifactReader(IReadOnlyDictionary<string, byte[]> artifacts) : IBenchmarkArtifactReader
    {
        public Task<ReadOnlyMemory<byte>> ReadAsync(string artifactReference, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!artifacts.TryGetValue(artifactReference, out var bytes))
            {
                throw new FileNotFoundException("Test artifact not found.", artifactReference);
            }

            return Task.FromResult<ReadOnlyMemory<byte>>(bytes);
        }
    }
}
