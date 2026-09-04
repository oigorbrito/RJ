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

        var catalog = JsonSerializer.Deserialize<ExternalGenerationBenchmarkCatalog>(json, JsonOptions)
            ?? throw new InvalidOperationException("External benchmark catalog JSON produced no document.");
        return catalog;
    }

    public static string ComputeSha256(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant();
    }

    private static GenerationEvaluationCase ConvertCase(ExternalGenerationBenchmarkCase source)
    {
        ArgumentNullException.ThrowIfNull(source);
        ValidateRequired(source.Id, "case.id");
        ValidateRequired(source.ContextCaseId, "case.contextCaseId");
        ValidateRequired(source.Query, "case.query");
        ValidateRequired(source.SourceReference, "case.sourceReference");
        ValidateSha256(source.SourceSha256, "case.sourceSha256");
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
            ValidateSha256(item.ContentSha256, "item.contentSha256");
            ValidateRequired(item.Excerpt, "item.excerpt");
            var position = SourcePosition.Create(item.StartOffset, item.Length, item.SourceLength);
            if (item.Excerpt.Length != position.Length)
            {
                throw new InvalidOperationException($"Benchmark case '{source.Id}' has an excerpt whose length does not match its source position.");
            }

            return new GenerationContextItem(
                source.ContextCaseId.Trim(),
                item.DocumentId.Trim(),
                item.SourceName.Trim(),
                item.ContentSha256.Trim().ToLowerInvariant(),
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
                var matches = items.Any(item =>
                    StringComparer.Ordinal.Equals(item.DocumentId, citation.DocumentId.Trim())
                    && StringComparer.Ordinal.Equals(item.ContentSha256, citation.ContentSha256.Trim().ToLowerInvariant())
                    && item.Position.StartOffset == citation.StartOffset
                    && item.Position.Length == citation.Length);

                if (!matches)
                {
                    throw new InvalidOperationException($"Benchmark case '{source.Id}' oracle citation does not match any context evidence item.");
                }

                return new GenerationCitation(
                    citation.DocumentId.Trim(),
                    citation.ContentSha256.Trim().ToLowerInvariant(),
                    citation.StartOffset,
                    citation.Length);
            }).ToArray();

            return new ExpectedGenerationClaim(claim.Text, citations);
        }).ToArray();

        if (source.ExpectAbstention && expectedClaims.Length != 0)
        {
            throw new InvalidOperationException($"Benchmark case '{source.Id}' cannot expect abstention and expected claims simultaneously.");
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
    string SourceReference,
    string SourceSha256,
    string OracleReference,
    string OracleSha256,
    bool ExpectAbstention,
    IReadOnlyList<ExternalGenerationContextItem> Items,
    IReadOnlyList<ExternalExpectedGenerationClaim> ExpectedClaims);

public sealed record ExternalGenerationContextItem(
    string DocumentId,
    string SourceName,
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
