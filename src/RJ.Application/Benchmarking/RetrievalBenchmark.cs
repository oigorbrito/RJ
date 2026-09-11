using System.Diagnostics;
using System.Text.Json;
using RJ.Application.Retrieval;
using RJ.Domain.Cases;

namespace RJ.Application.Benchmarking;

public sealed record RetrievalExpectedEvidence(string DocumentId, string ContentSha256)
{
    public string DocumentId { get; } = EmpiricalTreatmentDefinition.Require(DocumentId, nameof(DocumentId));
    public string ContentSha256 { get; } = EmpiricalTreatmentDefinition.RequireSha256(ContentSha256, nameof(ContentSha256));
}

public sealed record RetrievalBenchmarkQuery(
    string QueryId,
    string Query,
    IReadOnlyList<RetrievalExpectedEvidence> ExpectedEvidence)
{
    public string QueryId { get; } = EmpiricalTreatmentDefinition.Require(QueryId, nameof(QueryId));
    public string Query { get; } = EmpiricalTreatmentDefinition.Require(Query, nameof(Query));
    public IReadOnlyList<RetrievalExpectedEvidence> ExpectedEvidence { get; } =
        ExpectedEvidence ?? throw new ArgumentNullException(nameof(ExpectedEvidence));

    public void Validate()
    {
        if (ExpectedEvidence.Count == 0)
        {
            throw new InvalidOperationException($"Retrieval query '{QueryId}' must define expected evidence.");
        }

        if (ExpectedEvidence
            .Select(item => $"{item.DocumentId}\n{item.ContentSha256}")
            .Distinct(StringComparer.Ordinal)
            .Count() != ExpectedEvidence.Count)
        {
            throw new InvalidOperationException($"Retrieval query '{QueryId}' contains duplicate expected evidence.");
        }
    }
}

public sealed record RetrievalBenchmarkCase(
    string CaseId,
    IReadOnlyList<RetrievalBenchmarkQuery> Queries)
{
    public string CaseId { get; } = EmpiricalTreatmentDefinition.Require(CaseId, nameof(CaseId));
    public IReadOnlyList<RetrievalBenchmarkQuery> Queries { get; } =
        Queries ?? throw new ArgumentNullException(nameof(Queries));

    public void Validate()
    {
        if (Queries.Count == 0)
        {
            throw new InvalidOperationException($"Retrieval benchmark case '{CaseId}' must contain at least one query.");
        }

        if (Queries.Select(item => item.QueryId).Distinct(StringComparer.Ordinal).Count() != Queries.Count)
        {
            throw new InvalidOperationException($"Retrieval benchmark case '{CaseId}' contains duplicate query ids.");
        }

        foreach (var query in Queries)
        {
            query.Validate();
        }
    }
}

public sealed record RetrievalBenchmarkCatalog(
    string Version,
    IReadOnlyList<RetrievalBenchmarkCase> Cases)
{
    public string Version { get; } = EmpiricalTreatmentDefinition.Require(Version, nameof(Version));
    public IReadOnlyList<RetrievalBenchmarkCase> Cases { get; } =
        Cases ?? throw new ArgumentNullException(nameof(Cases));

    public void Validate()
    {
        if (Cases.Count == 0)
        {
            throw new InvalidOperationException("Retrieval benchmark catalog must contain at least one case.");
        }

        if (Cases.Select(item => item.CaseId).Distinct(StringComparer.Ordinal).Count() != Cases.Count)
        {
            throw new InvalidOperationException("Retrieval benchmark catalog contains duplicate case ids.");
        }

        foreach (var item in Cases)
        {
            item.Validate();
        }
    }
}

public sealed record RetrievalBenchmarkExecutionMetadata(string GitCommit, string Runtime)
{
    public string GitCommit { get; } = RequireGitCommit(GitCommit);
    public string Runtime { get; } = EmpiricalTreatmentDefinition.Require(Runtime, nameof(Runtime));

    private static string RequireGitCommit(string value)
    {
        var normalized = EmpiricalTreatmentDefinition.Require(value, nameof(GitCommit)).ToLowerInvariant();
        if (normalized.Length != 40 || normalized.Any(character => !Uri.IsHexDigit(character)))
        {
            throw new ArgumentException("Git commit must contain exactly 40 hexadecimal characters.", nameof(value));
        }

        return normalized;
    }
}

public sealed record RetrievalBenchmarkTreatmentMetadata(
    string TreatmentId,
    string ImplementationId,
    string ConfigurationReference,
    string ConfigurationSha256)
{
    public string TreatmentId { get; } = EmpiricalTreatmentDefinition.Require(TreatmentId, nameof(TreatmentId));
    public string ImplementationId { get; } = EmpiricalTreatmentDefinition.Require(ImplementationId, nameof(ImplementationId));
    public string ConfigurationReference { get; } = EmpiricalTreatmentDefinition.Require(ConfigurationReference, nameof(ConfigurationReference));
    public string ConfigurationSha256 { get; } = EmpiricalTreatmentDefinition.RequireSha256(ConfigurationSha256, nameof(ConfigurationSha256));
}

public sealed record RetrievalBenchmarkQueryReport(
    string QueryId,
    int? FirstRelevantRank,
    bool HitAt1,
    bool HitAt3,
    bool HitAt5);

public sealed record RetrievalBenchmarkCaseReport(
    string CaseId,
    int QueryCount,
    int HitAt1,
    int HitAt3,
    int HitAt5,
    double Mrr,
    double DurationMs,
    IReadOnlyList<RetrievalBenchmarkQueryReport> Queries,
    string? ErrorType,
    string? ErrorMessage)
{
    public bool ExecutionSucceeded => ErrorType is null;
}

public sealed record RetrievalBenchmarkReport(
    string FormatVersion,
    string CatalogVersion,
    RetrievalBenchmarkExecutionMetadata Execution,
    RetrievalBenchmarkTreatmentMetadata Treatment,
    IReadOnlyList<RetrievalBenchmarkCaseReport> Cases)
{
    public const string SupportedFormatVersion = "rjudi-retrieval-benchmark-report-v1";

    public RetrievalBenchmarkReport Validate()
    {
        if (!StringComparer.Ordinal.Equals(FormatVersion, SupportedFormatVersion))
        {
            throw new InvalidOperationException($"Unsupported retrieval benchmark report format '{FormatVersion}'.");
        }

        EmpiricalTreatmentDefinition.Require(CatalogVersion, nameof(CatalogVersion));
        ArgumentNullException.ThrowIfNull(Execution);
        ArgumentNullException.ThrowIfNull(Treatment);
        ArgumentNullException.ThrowIfNull(Cases);
        if (Cases.Count == 0)
        {
            throw new InvalidOperationException("Retrieval benchmark report must contain case reports.");
        }

        if (Cases.Select(item => item.CaseId).Distinct(StringComparer.Ordinal).Count() != Cases.Count)
        {
            throw new InvalidOperationException("Retrieval benchmark report contains duplicate case ids.");
        }

        foreach (var item in Cases)
        {
            ValidateCaseReport(item);
        }

        return this;
    }

    private static void ValidateCaseReport(RetrievalBenchmarkCaseReport item)
    {
        EmpiricalTreatmentDefinition.Require(item.CaseId, "caseId");
        ArgumentNullException.ThrowIfNull(item.Queries);
        if (!double.IsFinite(item.DurationMs) || item.DurationMs < 0)
        {
            throw new InvalidOperationException($"Retrieval case '{item.CaseId}' has invalid duration.");
        }

        if (!item.ExecutionSucceeded)
        {
            if (string.IsNullOrWhiteSpace(item.ErrorType)
                || string.IsNullOrWhiteSpace(item.ErrorMessage)
                || item.QueryCount != 0
                || item.Queries.Count != 0
                || item.HitAt1 != 0
                || item.HitAt3 != 0
                || item.HitAt5 != 0
                || item.Mrr != 0)
            {
                throw new InvalidOperationException($"Failed retrieval case '{item.CaseId}' contains inconsistent execution evidence.");
            }

            return;
        }

        if (!string.IsNullOrWhiteSpace(item.ErrorMessage))
        {
            throw new InvalidOperationException($"Successful retrieval case '{item.CaseId}' cannot contain an error message.");
        }

        if (item.QueryCount <= 0 || item.Queries.Count != item.QueryCount)
        {
            throw new InvalidOperationException($"Retrieval case '{item.CaseId}' has inconsistent query counts.");
        }

        if (item.Queries.Any(query => string.IsNullOrWhiteSpace(query.QueryId))
            || item.Queries.Select(query => query.QueryId).Distinct(StringComparer.Ordinal).Count() != item.Queries.Count)
        {
            throw new InvalidOperationException($"Retrieval case '{item.CaseId}' contains invalid or duplicate query reports.");
        }

        foreach (var query in item.Queries)
        {
            ValidateQueryReport(item.CaseId, query);
        }

        var hitAt1 = item.Queries.Count(query => query.HitAt1);
        var hitAt3 = item.Queries.Count(query => query.HitAt3);
        var hitAt5 = item.Queries.Count(query => query.HitAt5);
        if (item.HitAt1 != hitAt1 || item.HitAt3 != hitAt3 || item.HitAt5 != hitAt5)
        {
            throw new InvalidOperationException($"Retrieval case '{item.CaseId}' aggregate hit counts do not match query reports.");
        }

        var expectedMrr = item.Queries.Sum(query => query.FirstRelevantRank is int rank ? 1d / rank : 0d) / item.QueryCount;
        if (!double.IsFinite(item.Mrr) || item.Mrr < 0 || item.Mrr > 1 || Math.Abs(item.Mrr - expectedMrr) > 1e-12)
        {
            throw new InvalidOperationException($"Retrieval case '{item.CaseId}' MRR does not match query reports.");
        }
    }

    private static void ValidateQueryReport(string caseId, RetrievalBenchmarkQueryReport query)
    {
        if (query.FirstRelevantRank is null)
        {
            if (query.HitAt1 || query.HitAt3 || query.HitAt5)
            {
                throw new InvalidOperationException($"Retrieval case '{caseId}' query '{query.QueryId}' has hit flags without a relevant rank.");
            }

            return;
        }

        var rank = query.FirstRelevantRank.Value;
        if (rank <= 0
            || query.HitAt1 != (rank <= 1)
            || query.HitAt3 != (rank <= 3)
            || query.HitAt5 != (rank <= 5))
        {
            throw new InvalidOperationException($"Retrieval case '{caseId}' query '{query.QueryId}' has inconsistent rank/hit flags.");
        }
    }
}

public static class RetrievalBenchmarkJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public static string Serialize(RetrievalBenchmarkReport report) =>
        JsonSerializer.Serialize((report ?? throw new ArgumentNullException(nameof(report))).Validate(), Options);

    public static RetrievalBenchmarkReport Parse(ReadOnlySpan<byte> utf8Json)
    {
        if (utf8Json.IsEmpty)
        {
            throw new ArgumentException("Retrieval benchmark report cannot be empty.", nameof(utf8Json));
        }

        return (JsonSerializer.Deserialize<RetrievalBenchmarkReport>(utf8Json, Options)
            ?? throw new InvalidOperationException("Retrieval benchmark report produced no document.")).Validate();
    }
}

public sealed class RetrievalBenchmarkRunner(IIdentifiedLegalDocumentSearch search)
{
    private const string CandidateExecutionFailureMessage = "Retrieval treatment execution failed.";
    private readonly IIdentifiedLegalDocumentSearch _search = search ?? throw new ArgumentNullException(nameof(search));

    public async Task<RetrievalBenchmarkReport> RunAsync(
        RetrievalBenchmarkCatalog catalog,
        RetrievalBenchmarkExecutionMetadata execution,
        RetrievalBenchmarkTreatmentMetadata treatment,
        int limit,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(execution);
        ArgumentNullException.ThrowIfNull(treatment);
        catalog.Validate();
        if (limit is < 5 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(limit), limit, "Retrieval benchmark limit must be between 5 and 100.");
        }

        var implementationId = EmpiricalTreatmentDefinition.Require(_search.ImplementationId, "search.ImplementationId");
        if (!StringComparer.Ordinal.Equals(implementationId, treatment.ImplementationId))
        {
            throw new InvalidOperationException(
                $"Retrieval treatment implementation '{treatment.ImplementationId}' does not match executed search implementation '{implementationId}'.");
        }

        var reports = new List<RetrievalBenchmarkCaseReport>(catalog.Cases.Count);
        foreach (var benchmarkCase in catalog.Cases.OrderBy(item => item.CaseId, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var stopwatch = Stopwatch.StartNew();
            try
            {
                var queryReports = new List<RetrievalBenchmarkQueryReport>(benchmarkCase.Queries.Count);
                var hit1 = 0;
                var hit3 = 0;
                var hit5 = 0;
                var reciprocalRank = 0d;

                foreach (var query in benchmarkCase.Queries.OrderBy(item => item.QueryId, StringComparer.Ordinal))
                {
                    var hits = await _search.SearchAsync(new LegalCaseId(benchmarkCase.CaseId), query.Query, limit, cancellationToken);
                    ArgumentNullException.ThrowIfNull(hits);
                    RequireCaseScopedHits(benchmarkCase.CaseId, hits);
                    var firstRelevantRank = FindFirstRelevantRank(hits, query.ExpectedEvidence);
                    if (firstRelevantRank is int rank)
                    {
                        reciprocalRank += 1d / rank;
                        if (rank <= 1) hit1++;
                        if (rank <= 3) hit3++;
                        if (rank <= 5) hit5++;
                    }

                    queryReports.Add(new RetrievalBenchmarkQueryReport(
                        query.QueryId,
                        firstRelevantRank,
                        firstRelevantRank is <= 1,
                        firstRelevantRank is <= 3,
                        firstRelevantRank is <= 5));
                }

                stopwatch.Stop();
                reports.Add(new RetrievalBenchmarkCaseReport(
                    benchmarkCase.CaseId,
                    benchmarkCase.Queries.Count,
                    hit1,
                    hit3,
                    hit5,
                    reciprocalRank / benchmarkCase.Queries.Count,
                    stopwatch.Elapsed.TotalMilliseconds,
                    queryReports,
                    null,
                    null));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                stopwatch.Stop();
                reports.Add(new RetrievalBenchmarkCaseReport(
                    benchmarkCase.CaseId,
                    0,
                    0,
                    0,
                    0,
                    0,
                    stopwatch.Elapsed.TotalMilliseconds,
                    [],
                    exception.GetType().FullName,
                    CandidateExecutionFailureMessage));
            }
        }

        return new RetrievalBenchmarkReport(
            RetrievalBenchmarkReport.SupportedFormatVersion,
            catalog.Version,
            execution,
            treatment,
            reports).Validate();
    }

    private static void RequireCaseScopedHits(string caseId, IReadOnlyList<LegalDocumentSearchHit> hits)
    {
        foreach (var hit in hits)
        {
            if (hit is null || hit.Document is null)
            {
                throw new InvalidOperationException("Retrieval treatment returned a null hit/document.");
            }

            if (!StringComparer.Ordinal.Equals(hit.Document.CaseId, caseId))
            {
                throw new InvalidOperationException(
                    $"Retrieval treatment returned cross-case evidence for case '{caseId}'.");
            }
        }
    }

    private static int? FindFirstRelevantRank(
        IReadOnlyList<LegalDocumentSearchHit> hits,
        IReadOnlyList<RetrievalExpectedEvidence> expected)
    {
        for (var index = 0; index < hits.Count; index++)
        {
            var document = hits[index].Document;
            if (expected.Any(item =>
                StringComparer.Ordinal.Equals(item.DocumentId, document.DocumentId)
                && StringComparer.Ordinal.Equals(item.ContentSha256, document.ContentSha256)))
            {
                return index + 1;
            }
        }

        return null;
    }
}
