using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using RJ.Application.Benchmarking;
using RJ.Application.Evaluation;
using RJ.Application.Generation;
using RJ.Application.Retrieval;

namespace RJ.BenchmarkCli;

public static class OabRulingBrAbRunner
{
    private static readonly string[] StopWords =
    [
        "a", "as", "o", "os", "e", "ou", "de", "da", "do", "das", "dos", "em", "no", "na", "nos", "nas",
        "para", "por", "com", "sem", "ao", "aos", "ÃƒÂ ", "ÃƒÂ s", "um", "uma", "uns", "umas", "que", "qual",
        "quais", "quando", "onde", "como", "porque", "porquÃƒÂª", "se", "sobre", "entre", "noÃƒÂ§ÃƒÂµes", "oab",
        "questÃƒÂ£o", "questoes", "questÃƒÂ£o", "responda", "fundamente", "caso", "hipÃƒÂ³tese", "hipotese"
    ];
    public static async Task<int> RunAsync(
        IReadOnlyList<string> args,
        CancellationToken cancellationToken) =>
        await RunAsync(args, Console.Error, cancellationToken);

    public static async Task<int> RunAsync(
        IReadOnlyList<string> args,
        TextWriter errorWriter,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(errorWriter);

        try
        {
            var options = Parse(args);
            var oab = await OabBenchDemoCatalogAdapter.BuildAsync(options.OabRoot, options.RepositoryCommit, cancellationToken);
            var rulingbr = await RulingBrDemoCatalogAdapter.BuildAsync(options.RulingBrRoot, options.RepositoryCommit, cancellationToken);
            var rulingbrCorpusPath = await RulingBrDemoCatalogAdapter.ResolveCorpusPathAsync(options.RulingBrRoot, cancellationToken);
            var rulingbrCases = await RulingBrDemoCatalogAdapter.ReadCasesFromFileAsync(rulingbrCorpusPath, cancellationToken);
            var a = await RunBranchAsync(oab.CatalogPath, options, branchLabel: "A", withRetrieval: false, useChallenger: false, rulingbrCases, cancellationToken);
            var b = await RunBranchAsync(oab.CatalogPath, options, branchLabel: "B", withRetrieval: true, useChallenger: false, rulingbrCases, cancellationToken);
            var c = await RunBranchAsync(oab.CatalogPath, options, branchLabel: "C", withRetrieval: true, useChallenger: true, rulingbrCases, cancellationToken);

            var report = new OabRulingBrAbReport(
                "oab-rulingbr-ab-v1",
                options.RepositoryCommit,
                options.OabRoot,
                options.RulingBrRoot,
                options.TopK,
                options.ModelConfiguration,
                oab.CatalogSha256,
                rulingbr.CatalogSha256,
                a,
                b,
                c,
                c.TotalCases > 0 ? (double)c.RetrievalCoverageCases / c.TotalCases : 0.0,
                c.ZeroEvidenceCases,
                DateTimeOffset.UnixEpoch);

            await AtomicTextFileWriter.WriteAsync(options.ReportPath, JsonSerializer.Serialize(report, JsonOptions), cancellationToken);
            return 0;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (ArgumentException exception)
        {
            await errorWriter.WriteLineAsync($"{exception.GetType().Name}: {exception.Message}");
            return 2;
        }
        catch (Exception exception)
        {
            if (StringComparer.Ordinal.Equals(Environment.GetEnvironmentVariable("RJ_AB_DIAGNOSTICS"), "1"))
            {
                await errorWriter.WriteLineAsync(exception.ToString());
            }

            await errorWriter.WriteLineAsync($"{exception.GetType().Name}: OAB/RulingBR AB execution failed.");
            return 2;
        }
    }

    private static async Task<OabRulingBrAbBranchReport> RunBranchAsync(
        string catalogPath,
        OabRulingBrAbOptions options,
        string branchLabel,
        bool withRetrieval,
        bool useChallenger,
        IReadOnlyList<RulingBrDemoCatalogAdapter.RulingBrDemoCase> rulingbrCases,
        CancellationToken cancellationToken = default)
    {
        var external = ExternalGenerationBenchmarkCatalog.Parse(await File.ReadAllBytesAsync(catalogPath, cancellationToken));
        var catalog = external.ToBenchmarkCatalog();
        var evaluator = new GenerationEvaluator();
        IGenerationModel model = useChallenger
            ? new OabRulingBrGenerationChallengerModel(options.ModelConfiguration)
            : new OabBenchDemoGenerationModel();
        var caseReports = new List<OabRulingBrAbCaseReport>(catalog.Cases.Count);
        var retrievalCoverageCases = 0;
        var zeroEvidenceCases = 0;
        var hitsPerQuery = new List<int>(catalog.Cases.Count);

        foreach (var evaluationCase in catalog.Cases.OrderBy(item => item.Id, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var context = evaluationCase.Context;
            if (withRetrieval)
            {
                var retrieved = await RetrieveRulingBrEvidenceAsync(rulingbrCases, evaluationCase.Context.Query, options.TopK, cancellationToken);
                hitsPerQuery.Add(retrieved.Count);
                if (retrieved.Count == 0)
                {
                    zeroEvidenceCases++;
                }
                else
                {
                    retrievalCoverageCases++;
                    context = new GenerationContext(
                        context.CaseId,
                        context.Query,
                        context.CharacterBudget,
                        context.UsedCharacters + retrieved.Sum(item => item.Excerpt.Length),
                        context.Items.Concat(retrieved.Select(item => new GenerationContextItem(
                            context.CaseId,
                            item.DocumentId,
                            item.SourceName,
                            item.ContentSha256,
                            item.Excerpt,
                            item.Position,
                            item.Rank))).ToArray());
                }
            }

            var output = await model.GenerateAsync(context, cancellationToken);
            var evaluation = evaluator.Evaluate(evaluationCase, output);
            caseReports.Add(new OabRulingBrAbCaseReport(
                evaluationCase.Id,
                evaluation.Passed,
                evaluation.ClaimRecall,
                evaluation.CitationValidity,
                evaluation.Groundedness));
        }

        return new OabRulingBrAbBranchReport(
            branchLabel,
            caseReports.Count,
            caseReports.Count(item => item.Passed),
            caseReports.Count(item => !item.Passed),
            caseReports.Min(item => item.ClaimRecall),
            caseReports.Average(item => item.ClaimRecall),
            caseReports.Min(item => item.CitationValidity),
            caseReports.Average(item => item.CitationValidity),
            caseReports.Min(item => item.Groundedness),
            caseReports.Average(item => item.Groundedness),
            retrievalCoverageCases,
            zeroEvidenceCases,
            hitsPerQuery.Count == 0 ? 0.0 : hitsPerQuery.Average(),
            hitsPerQuery.Count == 0 ? 0 : hitsPerQuery.OrderBy(item => item).ElementAt(hitsPerQuery.Count / 2),
            caseReports);
    }

    private static async Task<IReadOnlyList<LegalEvidenceHit>> RetrieveRulingBrEvidenceAsync(
        IReadOnlyList<RulingBrDemoCatalogAdapter.RulingBrDemoCase> rulingbrCases,
        string query,
        int topK,
        CancellationToken cancellationToken)
    {
        var hits = new List<LegalEvidenceHit>();
        var lexicalQuery = FormulateLexicalQuery(query);
        if (lexicalQuery.Length == 0)
        {
            return hits;
        }

        foreach (var demoCase in rulingbrCases)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var content = BuildCorpusContent(demoCase);
            var score = ScoreLexicalMatch(content, lexicalQuery);
            if (score == 0)
            {
                continue;
            }

            var sha256 = ComputeSha256(Encoding.UTF8.GetBytes(content));
            var position = SourcePosition.Create(0, Math.Min(content.Length, 200), content.Length);
            hits.Add(new LegalEvidenceHit(
                "rulingbr",
                demoCase.Id,
                demoCase.Id,
                sha256,
                content.Substring(0, position.Length),
                position,
                score));
            if (hits.Count >= topK)
            {
                return hits.OrderByDescending(item => item.Rank).ThenBy(item => item.DocumentId, StringComparer.Ordinal).ToArray();
            }
        }

        return hits.OrderByDescending(item => item.Rank).ThenBy(item => item.DocumentId, StringComparer.Ordinal).ToArray();
    }

    private static string BuildCorpusContent(RulingBrDemoCatalogAdapter.RulingBrDemoCase demoCase)
    {
        return string.Join(Environment.NewLine, [demoCase.SourceText, demoCase.OracleText]).Trim();
    }

    private static string[] FormulateLexicalQuery(string query)
    {
        var normalized = Normalize(query);
        return normalized
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(token => token.Length > 2 && !StopWords.Contains(token, StringComparer.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .Take(12)
            .ToArray();
    }

    private static int ScoreLexicalMatch(string content, IReadOnlyList<string> terms)
    {
        var normalizedContent = Normalize(content);
        var score = 0;
        foreach (var term in terms)
        {
            if (normalizedContent.Contains(term, StringComparison.Ordinal))
            {
                score++;
            }
        }

        return score;
    }

    private static string Normalize(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (var character in text.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(character) || char.IsWhiteSpace(character))
            {
                builder.Append(character);
                continue;
            }

            builder.Append(' ');
        }

        return builder.ToString();
    }

    private static OabRulingBrAbOptions Parse(IReadOnlyList<string> args)
    {
        if (args.Count % 2 != 0)
        {
            throw new ArgumentException("Arguments must be provided as name/value pairs.", nameof(args));
        }

        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var index = 0; index < args.Count; index += 2)
        {
            if (!values.TryAdd(args[index], args[index + 1]))
            {
                throw new ArgumentException($"Duplicate argument: {args[index]}", nameof(args));
            }
        }

        string GetRequired(string name)
        {
            if (!values.TryGetValue(name, out var value) || string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException($"Required argument is missing or empty: {name}", nameof(args));
            }

            return value.Trim();
        }

        var topK = int.Parse(GetRequired("--top-k"), System.Globalization.CultureInfo.InvariantCulture);
        if (topK < 1)
        {
            throw new ArgumentException("--top-k must be at least 1.", nameof(args));
        }

        return new OabRulingBrAbOptions(
            GetRequired("--oab-root"),
            GetRequired("--rulingbr-root"),
            GetRequired("--report"),
            values.TryGetValue("--repo-commit", out var commit) ? commit.Trim() : null,
            topK,
            values.TryGetValue("--model-config", out var modelConfig) ? modelConfig.Trim() : string.Empty);
    }

    private static string ComputeSha256(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };
}

public sealed record OabRulingBrAbOptions(
    string OabRoot,
    string RulingBrRoot,
    string ReportPath,
    string? RepositoryCommit,
    int TopK,
    string ModelConfiguration);

public sealed record OabRulingBrAbReport(
    string RunnerVersion,
    string? RepositoryCommit,
    string OabRoot,
    string RulingBrRoot,
    int TopK,
    string ModelConfiguration,
    string OabCorpusSha256,
    string RulingBrCorpusSha256,
    OabRulingBrAbBranchReport A,
    OabRulingBrAbBranchReport B,
    OabRulingBrAbBranchReport C,
    double RetrievalCoverage,
    int ZeroEvidenceCases,
    DateTimeOffset Runtime);

public sealed record OabRulingBrAbBranchReport(
    string Branch,
    int TotalCases,
    int PassedCases,
    int FailedCases,
    double MinClaimRecall,
    double AvgClaimRecall,
    double MinCitationValidity,
    double AvgCitationValidity,
    double MinGroundedness,
    double AvgGroundedness,
    int RetrievalCoverageCases,
    int ZeroEvidenceCases,
    double AvgHitsPerQuery,
    int MedianHitsPerQuery,
    IReadOnlyList<OabRulingBrAbCaseReport> Cases);

public sealed record OabRulingBrAbCaseReport(
    string CaseId,
    bool Passed,
    double ClaimRecall,
    double CitationValidity,
    double Groundedness);
