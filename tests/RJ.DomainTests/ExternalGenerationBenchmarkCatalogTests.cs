using RJ.Application.Benchmarking;

namespace RJ.DomainTests;

public sealed class ExternalGenerationBenchmarkCatalogTests
{
    [Fact]
    public void Parse_and_convert_accepts_versioned_catalog_with_verifiable_oracle_citation()
    {
        var json = ValidCatalogJson();

        var external = ExternalGenerationBenchmarkCatalog.Parse(json);
        var catalog = external.ToBenchmarkCatalog();

        Assert.Equal("legal-corpus-2026-09-v1", catalog.Version);
        var evaluationCase = Assert.Single(catalog.Cases);
        Assert.Equal("fixture-001", evaluationCase.Id);
        var expected = Assert.Single(evaluationCase.ExpectedClaims);
        var citation = Assert.Single(expected.Citations);
        Assert.Equal("doc-1", citation.DocumentId);
        Assert.Equal(0, citation.StartOffset);
        Assert.Equal(8, citation.Length);
    }

    [Fact]
    public void Convert_rejects_oracle_citation_outside_context()
    {
        var json = ValidCatalogJson().Replace(
            "\"documentId\": \"doc-1\", \"contentSha256\": \"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\", \"startOffset\": 0, \"length\": 8",
            "\"documentId\": \"doc-x\", \"contentSha256\": \"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\", \"startOffset\": 0, \"length\": 8",
            StringComparison.Ordinal);

        var external = ExternalGenerationBenchmarkCatalog.Parse(json);

        Assert.Throws<InvalidOperationException>(() => external.ToBenchmarkCatalog());
    }

    [Fact]
    public void Convert_rejects_provenance_hash_that_does_not_match_content_hash()
    {
        var json = ValidCatalogJson().Replace(
            "\"sourceSha256\": \"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\"",
            "\"sourceSha256\": \"cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc\"",
            StringComparison.Ordinal);

        var external = ExternalGenerationBenchmarkCatalog.Parse(json);

        Assert.Throws<InvalidOperationException>(() => external.ToBenchmarkCatalog());
    }

    [Fact]
    public void Convert_rejects_unknown_format_version()
    {
        var json = ValidCatalogJson().Replace(
            ExternalGenerationBenchmarkCatalog.SupportedFormatVersion,
            "unsupported-v2",
            StringComparison.Ordinal);

        var external = ExternalGenerationBenchmarkCatalog.Parse(json);

        Assert.Throws<InvalidOperationException>(() => external.ToBenchmarkCatalog());
    }

    [Fact]
    public void ComputeSha256_is_deterministic_over_exact_utf8_catalog_bytes()
    {
        var json = ValidCatalogJson();

        var first = ExternalGenerationBenchmarkCatalog.ComputeSha256(json);
        var second = ExternalGenerationBenchmarkCatalog.ComputeSha256(json);

        Assert.Equal(first, second);
        Assert.Equal(64, first.Length);
        Assert.All(first, character => Assert.True(Uri.IsHexDigit(character)));
    }

    private static string ValidCatalogJson() => """
    {
      "formatVersion": "rj-generation-benchmark-catalog-v1",
      "catalogVersion": "legal-corpus-2026-09-v1",
      "cases": [
        {
          "id": "fixture-001",
          "contextCaseId": "case-1",
          "query": "Qual foi a decisão?",
          "oracleReference": "oracle://fixture-001/v1",
          "oracleSha256": "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
          "expectAbstention": false,
          "items": [
            {
              "documentId": "doc-1",
              "sourceName": "decisao.txt",
              "sourceReference": "source://fixture-001/doc-1",
              "sourceSha256": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
              "contentSha256": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
              "excerpt": "deferido",
              "startOffset": 0,
              "length": 8,
              "sourceLength": 8,
              "rank": 1.0
            }
          ],
          "expectedClaims": [
            {
              "text": "O pedido foi deferido.",
              "citations": [
                { "documentId": "doc-1", "contentSha256": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "startOffset": 0, "length": 8 }
              ]
            }
          ]
        }
      ]
    }
    """;
}
