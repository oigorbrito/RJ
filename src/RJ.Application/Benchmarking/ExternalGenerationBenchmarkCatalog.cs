using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using RJ.Application.Evaluation;
using RJ.Application.Generation;
using RJ.Application.Retrieval;

namespace RJ.Application.Benchmarking;

public sealed record ExternalGenerationBenchmarkCatalog(
    string FormatVersion,
    string CatalogVersion,
    IReadOnlyList<ExternalGenerationBenchmarkCase> Cases)
{
    public const string SupportedFormatVersion = "rj-generation-benchmark-catalog-v1";

    public GenerationBenchmarkCatalog ToBenchmarkCatalog()
    {
        if (!StringComparer.Ordinal.Equals(FormatVersion, SupportedFormatVersion))
        {
            throw new InvalidOperationException($"Unsupported benchmark catalog format version '{FormatVersion}'.");
        }

        if (string.IsNullOrWhiteSpace(CatalogVersion))
        {
            throw new InvalidOperationException("External benchmark catalog version cannot be empty.");
        }

        ArgumentNullException.ThrowIfNull(Cases);
        if (Cases.Count == 0)
        {
            throw new InvalidOperationException("External benchmark catalog must contain at least one case.");
        }

        var converted = Cases.Select(ConvertCase).ToArray();
        var catalog = new GenerationBenchmarkCatalog(CatalogVersion.Trim(), converted);
        catalog.Validate();
        return catalog;
    }

    public static ExternalGenerationBenchmarkCatalog Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new ArgumentException("External benchmark catalog JSON cannot be empty.", nameof(json));
        }

        return Parse(Encoding.UTF8.GetBytes(json));
    }

    public static ExternalGenerationBenchmarkCatalog Parse(ReadOnlySpan<byte> utf8Json)
    {
        if (utf8Json.IsEmpty)
        {
            throw new ArgumentException("External benchmark catalog UTF-8 payload cannot be empty.", nameof(utf8Json));
        }

        var catalog = JsonSerializer.Deserialize<ExternalGenerationBenchmarkCatalog>(utf8Json, JsonOptions)
            ?? throw new InvalidOperationException("External benchmark catalog JSON produced no document.");
        return catalog;
    }

    public static string ComputeSha256(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        return ComputeSha256(Encoding.UTF8.GetBytes(json));
    }

    public static string ComputeSha256(ReadOnlySpan<byte> bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static GenerationEvaluationCase ConvertCase(ExternalGenerationBenchmarkCase source)
    {
        ArgumentNullException.ThrowIfNull(source);
        ValidateRequired(source.Id, "case.id");
        ValidateRequired(source.ContextCaseId, "case.contextCaseId");
        ValidateRequired(source.Query, "case.query");
        ValidateRequired(source.OracleReference, "case.oracleReference");
        ValidateSha256(source.OracleSha256, "case.oracleSha256");
        ArgumentNullException.ThrowIfNull(source.Items);
        ArgumentNullException.ThrowIfNull(source.ExpectedClaims);

        if (source.Items.Count == 0)
        {
            throw new InvalidOperationException($"Benchmark case '{source.Id}' must contain at least one cited context item.");
        }

        var items = source.Items.Select(item =>
        {
            ArgumentNullException.ThrowIfNull(item);
            ValidateRequired(item.DocumentId, "item.documentId");
            ValidateRequired(item.SourceName, "item.sourceName");
            ValidateRequired(item.SourceReference, "item.sourceReference");
            ValidateSha256(item.SourceSha256, "item.sourceSha256");
            ValidateSha256(item.ContentSha256, "item.contentSha256");
            ValidateRequired(item.Excerpt, "item.excerpt");

            var sourceSha256 = item.SourceSha256.Trim().ToLowerInvariant();
            var contentSha256 = item.ContentSha256.Trim().ToLowerInvariant();
            if (!StringComparer.Ordinal.Equals(sourceSha256, contentSha256))
            {
                throw new InvalidOperationException($"Benchmark case '{source.Id}' evidence provenance hash must equal its content hash.");
            }

            var position = SourcePosition.Create(item.StartOffset, item.Length, item.SourceLength);
            if (item.Excerpt.Length != position.Length)
            {
                throw new InvalidOperationException($"Benchmark case '{source.Id}' has an excerpt whose length does not match its source position.");
            }

            return new GenerationContextItem(
                source.ContextCaseId.Trim(),
                item.DocumentId.Trim(),
                item.SourceName.Trim(),
                contentSha256,
                item.Excerpt,
                position,
                item.Rank);
        }).ToArray();

        var usedCharacters = items.Sum(item => item.Excerpt.Length);
        var context = new GenerationContext(
            source.ContextCaseId.Trim(),
            source.Query.Trim(),
            usedCharacters,
            usedCharacters,
            items);

        var expectedClaims = source.ExpectedClaims.Select(claim =>
        {
            ArgumentNullException.ThrowIfNull(claim);
            ValidateRequired(claim.Text, "expectedClaim.text");
            ArgumentNullException.ThrowIfNull(claim.Citations);

            if (!source.ExpectAbstention && claim.Citations.Count == 0)
            {
                throw new InvalidOperationException($"Benchmark case '{source.Id}' expected claim must include at least one citation.");
            }

            var citations = claim.Citations.Select(citation =>
            {
                ArgumentNullException.ThrowIfNull(citation);
                ValidateRequired(citation.DocumentId, "citation.documentId");
                ValidateSha256(citation.ContentSha256, "citation.contentSha256");
                var normalizedDocumentId = citation.DocumentId.Trim();
                var normalizedHash = citation.ContentSha256.Trim().ToLowerInvariant();
                var matches = items.Any(item =>
                    StringComparer.Ordinal.Equals(item.DocumentId, normalizedDocumentId)
                    && StringComparer.Ordinal.Equals(item.ContentSha256, normalizedHash)
                    && item.Position.StartOffset == citation.StartOffset
                    && item.Position.Length == citation.Length);

                if (!matches)
                {
                    throw new InvalidOperationException($"Benchmark case '{source.Id}' oracle citation does not match any context evidence item.");
                }

                return new GenerationCitation(
                    normalizedDocumentId,
                    normalizedHash,
                    citation.StartOffset,
                    citation.Length);
            }).ToArray();

            return new ExpectedGenerationClaim(claim.Text, citations);
        }).ToArray();

        if (source.ExpectAbstention && expectedClaims.Length != 0)
        {
            throw new InvalidOperationException($"Benchmark case '{source.Id}' cannot expect abstention and expected claims simultaneously.");
        }

        if (!source.ExpectAbstention && expectedClaims.Length == 0)
        {
            throw new InvalidOperationException($"Benchmark case '{source.Id}' must define expected claims when abstention is not expected.");
        }

        return new GenerationEvaluationCase(source.Id.Trim(), context, expectedClaims, source.ExpectAbstention);
    }

    private static void ValidateRequired(string value, string field)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"Required benchmark field '{field}' cannot be empty.");
        }
    }

    private static void ValidateSha256(string value, string field)
    {
        ValidateRequired(value, field);
        if (value.Length != 64 || value.Any(character => !Uri.IsHexDigit(character)))
        {
            throw new InvalidOperationException($"Benchmark field '{field}' must contain exactly 64 hexadecimal SHA-256 characters.");
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };
}

public sealed record ExternalGenerationBenchmarkCase(
    string Id,
    string ContextCaseId,
    string Query,
    string OracleReference,
    string OracleSha256,
    bool ExpectAbstention,
    IReadOnlyList<ExternalGenerationContextItem> Items,
    IReadOnlyList<ExternalExpectedGenerationClaim> ExpectedClaims);

public sealed record ExternalGenerationContextItem(
    string DocumentId,
    string SourceName,
    string SourceReference,
    string SourceSha256,
    string ContentSha256,
    string Excerpt,
    int StartOffset,
    int Length,
    int SourceLength,
    float Rank);

public sealed record ExternalExpectedGenerationClaim(
    string Text,
    IReadOnlyList<ExternalGenerationCitation> Citations);

public sealed record ExternalGenerationCitation(
    string DocumentId,
    string ContentSha256,
    int StartOffset,
    int Length);
