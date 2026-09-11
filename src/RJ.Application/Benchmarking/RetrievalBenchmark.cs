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

        if (ExpectedEvidence.Any(item => item is null))
        {
            throw new InvalidOperationException($"Retrieval query '{QueryId}' contains null expected evidence.");
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

        if (Queries.Any(item => item is null))
        {
            throw new InvalidOperationException($"Retrieval benchmark case '{CaseId}' contains a null query.");
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

        if (Cases.Any(item => item is null))
        {
            throw new InvalidOperationException("Retrieval benchmark catalog contains a null case.");
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
        ArgumentNullException.ThrowIfNull(Treatment);
        ArgumentNullException.ThrowIfNull(Cases);
        if (Cases.Count == 0 || Cases.Any(item => item is null))
        {
            throw new InvalidOperationException("Retrieval benchmark report must contain non-null case reports.");
        }

        if (Cases.Select(item => item.CaseId).Distinct(StringComparer.Ordinal).Count() != Cases.Count)
        {
            throw new InvalidOperationException("Retrieval benchmark report contains duplicate case ids.");
        }

        foreach (var item in Cases)
        {
            EmpiricalTreatmentDefinition.Require(item.CaseId, "caseId");
            if (!item.ExecutionSucceeded)
            {
                if (item.QueryCount != 0 || item.Queries.Count != 0 || item.HitAt1 != 0 || item.HitAt3 != 0 || item.HitAt5 != 0 || item.Mrr != 0 || item.DurationMs < 0)
                {
                    throw new InvalidOperationException($"Failed retrieval case '{item.CaseId}' contains inconsistent measurements.");
                }
                continue;
            }

            if (item.QueryCount <= 0 || item.Queries.Count != item.QueryCount)
            {
                throw new InvalidOperationException($"Retrieval case '{item.CaseId}' has inconsistent query counts.");
            }
            if (item.HitAt1 < 0 || item.HitAt1 > item.HitAt3 || item.HitAt3 > item.HitAt5 || item.HitAt5 > item.QueryCount)
            {
                throw new InvalidOperationException($"Retrieval case '{item.CaseId}' has invalid hit counts.");
            }
            if (!double.IsFinite(item.Mrr) || item.Mrr < 0 || item.Mrr > 1 || !double.IsFinite(item.DurationMs) || item.DurationMs < 0)
            {
                throw new InvalidOperationException($"Retrieval case '{item.CaseId}' has invalid numeric measurements.");
            }
            if (item.Queries.Select(query => query.QueryId).Distinct(StringComparer.Ordinal).Count() != item.Queries.Count)
            {
                throw new InvalidOperationException($"Retrieval case '{item.CaseId}' contains duplicate query reports.");
            }
        }

        return this;
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

public sealed class RetrievalBenchmarkRunner(ILegalDocumentSearch search)
{
    private const string CandidateExecutionFailureMessage = "Retrieval treatment execution failed.";
    private readonly ILegalDocumentSearch _search = search ?? throw new ArgumentNullException(nameof(search));

    public async Task<RetrievalBenchmarkReport> RunAsync(
        RetrievalBenchmarkCatalog catalog,
        RetrievalBenchmarkTreatmentMetadata treatment,
        int limit,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(treatment);
        catalog.Validate();
        if (limit < 5)
        {
            throw new ArgumentOutOfRangeException(nameof(limit), limit, "Retrieval benchmark limit must be at least 5 to measure hit@5.");
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
            treatment,
            reports).Validate();
    }

    private static int? FindFirstRelevantRank(
        IReadOnlyList<LegalDocumentSearchHit> hits,
        IReadOnlyList<RetrievalExpectedEvidence> expected)
    {
        for (var index = 0; index < hits.Count; index++)
        {
            var document = hits[index]?.Document;
            if (document is null)
            {
                continue;
            }

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
