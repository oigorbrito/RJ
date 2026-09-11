using System.Text;
using System.Text.Json;
using RJ.Domain.Cases;

namespace RJ.Application.Benchmarking;

public sealed record Eval010CorpusManifest(
    string FormatVersion,
    string CorpusVersion,
    DateTimeOffset FrozenAt,
    IReadOnlyList<Eval010CorpusCase> Cases)
{
    public const string SupportedFormatVersion = "rjudi-eval010-corpus-v1";
    public const int MinimumCaseCount = 30;
    public const int MaximumCaseCount = 50;

    public static Eval010CorpusManifest Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new ArgumentException("EVAL-010 corpus manifest JSON cannot be empty.", nameof(json));
        }

        return Parse(Encoding.UTF8.GetBytes(json));
    }

    public static Eval010CorpusManifest Parse(ReadOnlySpan<byte> utf8Json)
    {
        if (utf8Json.IsEmpty)
        {
            throw new ArgumentException("EVAL-010 corpus manifest payload cannot be empty.", nameof(utf8Json));
        }

        return JsonSerializer.Deserialize<Eval010CorpusManifest>(utf8Json, JsonOptions)
            ?? throw new InvalidOperationException("EVAL-010 corpus manifest produced no document.");
    }

    public void Validate()
    {
        if (!StringComparer.Ordinal.Equals(FormatVersion, SupportedFormatVersion))
        {
            throw new InvalidOperationException($"Unsupported EVAL-010 corpus format '{FormatVersion}'.");
        }

        Require(CorpusVersion, "corpusVersion");
        if (FrozenAt == default)
        {
            throw new InvalidOperationException("EVAL-010 frozenAt must be recorded.");
        }

        ArgumentNullException.ThrowIfNull(Cases);
        if (Cases.Count < MinimumCaseCount || Cases.Count > MaximumCaseCount)
        {
            throw new InvalidOperationException(
                $"EVAL-010 corpus must contain between {MinimumCaseCount} and {MaximumCaseCount} admitted cases; observed {Cases.Count}.");
        }

        var caseIds = new HashSet<string>(StringComparer.Ordinal);
        var cnjs = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in Cases)
        {
            ArgumentNullException.ThrowIfNull(item);
            var caseId = Require(item.CaseId, "case.caseId");
            var cnj = NormalizeCnj(item.Cnj);
            if (!caseIds.Add(caseId))
            {
                throw new InvalidOperationException($"Duplicate EVAL-010 case id '{caseId}'.");
            }

            if (!cnjs.Add(cnj))
            {
                throw new InvalidOperationException($"Duplicate EVAL-010 CNJ '{cnj}'.");
            }

            var sourceReference = Require(item.SourceReference, "case.sourceReference");
            var oracleReference = Require(item.OracleReference, "case.oracleReference");
            var reviewReference = Require(item.OracleReviewReference, "case.oracleReviewReference");
            ValidateSha256(item.SourceSha256, "case.sourceSha256");
            ValidateSha256(item.OracleSha256, "case.oracleSha256");
            ValidateSha256(item.OracleReviewSha256, "case.oracleReviewSha256");

            if (StringComparer.Ordinal.Equals(sourceReference, oracleReference)
                || StringComparer.Ordinal.Equals(sourceReference, reviewReference)
                || StringComparer.Ordinal.Equals(oracleReference, reviewReference))
            {
                throw new InvalidOperationException($"EVAL-010 case '{caseId}' source, oracle and review references must be distinct artifacts.");
            }

            var author = Require(item.OracleAuthorId, "case.oracleAuthorId");
            var reviewer = Require(item.OracleReviewerId, "case.oracleReviewerId");
            if (StringComparer.Ordinal.Equals(author, reviewer))
            {
                throw new InvalidOperationException($"EVAL-010 case '{caseId}' oracle author and reviewer must be distinct identifiers.");
            }

            if (item.OracleReviewedAt == default)
            {
                throw new InvalidOperationException($"EVAL-010 case '{caseId}' oracleReviewedAt must be recorded.");
            }
        }
    }

    public static string NormalizeCnj(string value)
    {
        try
        {
            return new LegalCaseCnj(Require(value, "cnj")).Digits;
        }
        catch (ArgumentException exception)
        {
            throw new InvalidOperationException("EVAL-010 CNJ is not a valid canonical CNJ number.", exception);
        }
    }

    private static string Require(string value, string field)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"Required EVAL-010 field '{field}' cannot be empty.");
        }

        return value.Trim();
    }

    private static void ValidateSha256(string value, string field)
    {
        var normalized = Require(value, field);
        if (normalized.Length != 64 || normalized.Any(character => !Uri.IsHexDigit(character)))
        {
            throw new InvalidOperationException($"EVAL-010 field '{field}' must contain exactly 64 hexadecimal SHA-256 characters.");
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };
}

public sealed record Eval010CorpusCase(
    string CaseId,
    string Cnj,
    string SourceReference,
    string SourceSha256,
    string OracleReference,
    string OracleSha256,
    string OracleReviewReference,
    string OracleReviewSha256,
    string OracleAuthorId,
    string OracleReviewerId,
    DateTimeOffset OracleReviewedAt);
