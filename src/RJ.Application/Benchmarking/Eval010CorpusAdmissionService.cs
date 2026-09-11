namespace RJ.Application.Benchmarking;

public sealed class Eval010CorpusAdmissionService(IBenchmarkArtifactReader artifactReader)
{
    private readonly IBenchmarkArtifactReader _artifactReader = artifactReader ?? throw new ArgumentNullException(nameof(artifactReader));

    public async Task<Eval010CorpusAdmissionReport> AdmitAsync(
        Eval010CorpusManifest manifest,
        ExternalGenerationBenchmarkCatalog? benchmarkCatalog,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        manifest.Validate();

        if (benchmarkCatalog is not null)
        {
            _ = benchmarkCatalog.ToBenchmarkCatalog();
            ValidateOracleIsolation(manifest, benchmarkCatalog);
        }

        var reports = new List<Eval010CorpusCaseAdmissionReport>(manifest.Cases.Count);
        foreach (var item in manifest.Cases.OrderBy(value => value.CaseId, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var failures = new List<Eval010CorpusAdmissionFailure>();
            var sourceVerified = await VerifyHashAsync(item.SourceReference, item.SourceSha256, "source-sha256", failures, cancellationToken);
            var oracleVerified = await VerifyHashAsync(item.OracleReference, item.OracleSha256, "oracle-sha256", failures, cancellationToken);
            var reviewVerified = await VerifyHashAsync(item.OracleReviewReference, item.OracleReviewSha256, "oracle-review-sha256", failures, cancellationToken);

            reports.Add(new Eval010CorpusCaseAdmissionReport(
                item.CaseId,
                Eval010CorpusManifest.NormalizeCnj(item.Cnj),
                sourceVerified,
                oracleVerified,
                reviewVerified,
                sourceVerified && oracleVerified && reviewVerified && failures.Count == 0,
                failures));
        }

        return new Eval010CorpusAdmissionReport(
            manifest.CorpusVersion.Trim(),
            manifest.FrozenAt,
            reports.Count,
            reports.All(item => item.Passed),
            reports);
    }

    public static void ValidateOracleIsolation(
        Eval010CorpusManifest manifest,
        ExternalGenerationBenchmarkCatalog benchmarkCatalog)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(benchmarkCatalog);
        manifest.Validate();
        _ = benchmarkCatalog.ToBenchmarkCatalog();

        var manifestById = manifest.Cases.ToDictionary(item => item.CaseId.Trim(), StringComparer.Ordinal);
        if (benchmarkCatalog.Cases.Count != manifest.Cases.Count)
        {
            throw new InvalidOperationException(
                $"EVAL-010 benchmark catalog case count {benchmarkCatalog.Cases.Count} does not match frozen corpus case count {manifest.Cases.Count}.");
        }

        var forbiddenEvidenceReferences = manifest.Cases
            .SelectMany(item => new[] { item.OracleReference.Trim(), item.OracleReviewReference.Trim() })
            .ToHashSet(StringComparer.Ordinal);

        foreach (var benchmarkCase in benchmarkCatalog.Cases)
        {
            if (!manifestById.TryGetValue(benchmarkCase.Id.Trim(), out var admitted))
            {
                throw new InvalidOperationException($"Benchmark case '{benchmarkCase.Id}' is not present in the frozen EVAL-010 corpus.");
            }

            if (!StringComparer.Ordinal.Equals(benchmarkCase.OracleReference.Trim(), admitted.OracleReference.Trim())
                || !StringComparer.Ordinal.Equals(
                    benchmarkCase.OracleSha256.Trim().ToLowerInvariant(),
                    admitted.OracleSha256.Trim().ToLowerInvariant()))
            {
                throw new InvalidOperationException($"Benchmark case '{benchmarkCase.Id}' oracle reference/hash does not match the frozen EVAL-010 manifest.");
            }

            foreach (var evidence in benchmarkCase.Items)
            {
                if (forbiddenEvidenceReferences.Contains(evidence.SourceReference.Trim()))
                {
                    throw new InvalidOperationException(
                        $"Oracle isolation violation: benchmark evidence for case '{benchmarkCase.Id}' references an oracle or oracle-review artifact.");
                }
            }
        }
    }

    private async Task<bool> VerifyHashAsync(
        string artifactReference,
        string expectedSha256,
        string gate,
        List<Eval010CorpusAdmissionFailure> failures,
        CancellationToken cancellationToken)
    {
        try
        {
            var bytes = await _artifactReader.ReadAsync(artifactReference.Trim(), cancellationToken);
            var actual = ExternalGenerationBenchmarkCatalog.ComputeSha256(bytes.Span);
            var expected = expectedSha256.Trim().ToLowerInvariant();
            if (StringComparer.Ordinal.Equals(actual, expected))
            {
                return true;
            }

            failures.Add(new Eval010CorpusAdmissionFailure(
                artifactReference.Trim(),
                gate,
                $"Expected {expected}, observed {actual}."));
            return false;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            failures.Add(new Eval010CorpusAdmissionFailure(
                artifactReference.Trim(),
                "artifact-read",
                $"{exception.GetType().Name}: Artifact could not be read."));
            return false;
        }
    }
}

public sealed record Eval010CorpusAdmissionReport(
    string CorpusVersion,
    DateTimeOffset FrozenAt,
    int CaseCount,
    bool Passed,
    IReadOnlyList<Eval010CorpusCaseAdmissionReport> Cases);

public sealed record Eval010CorpusCaseAdmissionReport(
    string CaseId,
    string Cnj,
    bool SourceVerified,
    bool OracleVerified,
    bool OracleReviewVerified,
    bool Passed,
    IReadOnlyList<Eval010CorpusAdmissionFailure> Failures);

public sealed record Eval010CorpusAdmissionFailure(
    string ArtifactReference,
    string Gate,
    string Message);
