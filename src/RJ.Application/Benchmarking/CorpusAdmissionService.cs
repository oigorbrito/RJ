using System.Text;

namespace RJ.Application.Benchmarking;

public sealed class CorpusAdmissionService(IBenchmarkArtifactReader artifactReader)
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private readonly IBenchmarkArtifactReader _artifactReader = artifactReader ?? throw new ArgumentNullException(nameof(artifactReader));

    public async Task<CorpusAdmissionReport> AdmitAsync(
        ExternalGenerationBenchmarkCatalog externalCatalog,
        string catalogSha256,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(externalCatalog);
        ValidateSha256(catalogSha256, nameof(catalogSha256));

        _ = externalCatalog.ToBenchmarkCatalog();

        var caseReports = new List<CorpusAdmissionCaseReport>(externalCatalog.Cases.Count);
        var totalEvidenceItems = 0;
        var verifiedEvidenceItems = 0;
        var verifiedOracles = 0;

        foreach (var benchmarkCase in externalCatalog.Cases.OrderBy(item => item.Id, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var failures = new List<CorpusAdmissionFailure>();
            var oracleVerified = await VerifyHashAsync(
                benchmarkCase.OracleReference,
                benchmarkCase.OracleSha256,
                "oracle-sha256",
                failures,
                cancellationToken);

            if (oracleVerified)
            {
                verifiedOracles++;
            }

            var verifiedInCase = 0;
            totalEvidenceItems += benchmarkCase.Items.Count;

            foreach (var item in benchmarkCase.Items)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    var bytes = await _artifactReader.ReadAsync(item.SourceReference, cancellationToken);
                    var actualHash = ExternalGenerationBenchmarkCatalog.ComputeSha256(bytes.Span);
                    if (!StringComparer.Ordinal.Equals(actualHash, item.SourceSha256.Trim().ToLowerInvariant()))
                    {
                        failures.Add(new CorpusAdmissionFailure(
                            item.SourceReference,
                            "source-sha256",
                            $"Expected {item.SourceSha256.Trim().ToLowerInvariant()}, observed {actualHash}."));
                        continue;
                    }

                    string sourceText;
                    try
                    {
                        sourceText = StrictUtf8.GetString(bytes.Span);
                    }
                    catch (DecoderFallbackException)
                    {
                        failures.Add(new CorpusAdmissionFailure(
                            item.SourceReference,
                            "source-utf8",
                            "Resolved source artifact is not valid strict UTF-8."));
                        continue;
                    }

                    if (sourceText.Length != item.SourceLength)
                    {
                        failures.Add(new CorpusAdmissionFailure(
                            item.SourceReference,
                            "source-length",
                            $"Expected UTF-16 length {item.SourceLength}, observed {sourceText.Length}."));
                        continue;
                    }

                    if (item.StartOffset < 0
                        || item.Length <= 0
                        || item.StartOffset > sourceText.Length - item.Length)
                    {
                        failures.Add(new CorpusAdmissionFailure(
                            item.SourceReference,
                            "source-position",
                            "Excerpt source position is outside the resolved source text."));
                        continue;
                    }

                    var reproduced = sourceText.Substring(item.StartOffset, item.Length);
                    if (!StringComparer.Ordinal.Equals(reproduced, item.Excerpt))
                    {
                        failures.Add(new CorpusAdmissionFailure(
                            item.SourceReference,
                            "excerpt-reproduction",
                            "Resolved source substring does not exactly match the catalog excerpt."));
                        continue;
                    }

                    verifiedInCase++;
                    verifiedEvidenceItems++;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    failures.Add(new CorpusAdmissionFailure(
                        item.SourceReference,
                        "source-read",
                        $"{exception.GetType().Name}: Source artifact could not be read."));
                }
            }

            var casePassed = oracleVerified
                && verifiedInCase == benchmarkCase.Items.Count
                && failures.Count == 0;

            caseReports.Add(new CorpusAdmissionCaseReport(
                benchmarkCase.Id,
                oracleVerified,
                benchmarkCase.Items.Count,
                verifiedInCase,
                casePassed,
                failures));
        }

        var passed = caseReports.Count > 0 && caseReports.All(item => item.Passed);
        return new CorpusAdmissionReport(
            externalCatalog.CatalogVersion.Trim(),
            catalogSha256.Trim().ToLowerInvariant(),
            caseReports.Count,
            totalEvidenceItems,
            verifiedEvidenceItems,
            verifiedOracles,
            passed,
            caseReports);
    }

    private async Task<bool> VerifyHashAsync(
        string artifactReference,
        string expectedSha256,
        string gate,
        List<CorpusAdmissionFailure> failures,
        CancellationToken cancellationToken)
    {
        try
        {
            var bytes = await _artifactReader.ReadAsync(artifactReference, cancellationToken);
            var actualHash = ExternalGenerationBenchmarkCatalog.ComputeSha256(bytes.Span);
            var normalizedExpected = expectedSha256.Trim().ToLowerInvariant();
            if (StringComparer.Ordinal.Equals(actualHash, normalizedExpected))
            {
                return true;
            }

            failures.Add(new CorpusAdmissionFailure(
                artifactReference,
                gate,
                $"Expected {normalizedExpected}, observed {actualHash}."));
            return false;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            failures.Add(new CorpusAdmissionFailure(
                artifactReference,
                "artifact-read",
                $"{exception.GetType().Name}: Artifact could not be read."));
            return false;
        }
    }

    private static void ValidateSha256(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.Length != 64
            || value.Any(character => !Uri.IsHexDigit(character)))
        {
            throw new ArgumentException("SHA-256 value must contain exactly 64 hexadecimal characters.", parameterName);
        }
    }
}
