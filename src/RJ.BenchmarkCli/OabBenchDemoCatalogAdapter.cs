using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using RJ.Application.Benchmarking;

namespace RJ.BenchmarkCli;

public sealed record OabBenchDemoCatalogInfo(
    string CatalogPath,
    string CatalogSha256,
    string CorpusRoot,
    string QuestionJsonlSha256,
    string GuidelinesJsonlSha256,
    string? JudgePromptsJsonlSha256,
    string? RepositoryCommit,
    string[] UsedArtifactPaths);

public static class OabBenchDemoCatalogAdapter
{
    public const string EnvironmentVariable = "RJ_LEGAL_DEMO_CORPUS";
    private static readonly UTF8Encoding Utf8WithoutBom = new(false);

    public static async Task<OabBenchDemoCatalogInfo> BuildAsync(
        string corpusRoot,
        string? repositoryCommit,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(corpusRoot))
        {
            throw new ArgumentException("Corpus root cannot be empty.", nameof(corpusRoot));
        }

        var normalizedCorpusRoot = Path.GetFullPath(corpusRoot);
        var questionPath = Path.Combine(normalizedCorpusRoot, "data", "oab_bench", "question.jsonl");
        var guidelinesPath = Path.Combine(normalizedCorpusRoot, "data", "oab_bench", "reference_answer", "guidelines.jsonl");
        var judgePromptsPath = Path.Combine(normalizedCorpusRoot, "data", "judge_prompts.jsonl");

        var questionLines = await File.ReadAllLinesAsync(questionPath, cancellationToken);
        var guidelineLines = await File.ReadAllLinesAsync(guidelinesPath, cancellationToken);
        var questions = questionLines.Where(line => !string.IsNullOrWhiteSpace(line)).Select(ParseQuestionRow)
            .ToDictionary(row => row.QuestionId, StringComparer.Ordinal);
        var guidelines = guidelineLines.Where(line => !string.IsNullOrWhiteSpace(line)).Select(ParseGuidelineRow)
            .ToDictionary(row => row.QuestionId, StringComparer.Ordinal);

        var outputRoot = Path.Combine(Path.GetTempPath(), $"rj-oab-bench-adapter-{Guid.NewGuid():N}");
        Directory.CreateDirectory(outputRoot);
        var sourceRoot = Path.Combine(outputRoot, "source");
        var oracleRoot = Path.Combine(outputRoot, "oracle");
        Directory.CreateDirectory(sourceRoot);
        Directory.CreateDirectory(oracleRoot);

        var cases = new List<ExternalGenerationBenchmarkCase>();
        foreach (var question in questions.Values.OrderBy(item => item.QuestionId, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!guidelines.TryGetValue(question.QuestionId, out var guideline))
            {
                continue;
            }

            var sourceText = question.Statement.Trim();
            var oracleText = string.Join(Environment.NewLine + Environment.NewLine, guideline.Turns);
            var sourceFile = Path.Combine(sourceRoot, $"{question.QuestionId}.txt");
            var oracleFile = Path.Combine(oracleRoot, $"{question.QuestionId}.txt");
            await File.WriteAllTextAsync(sourceFile, sourceText, Utf8WithoutBom, cancellationToken);
            await File.WriteAllTextAsync(oracleFile, oracleText, Utf8WithoutBom, cancellationToken);

            var sourceSha256 = ComputeSha256(await File.ReadAllBytesAsync(sourceFile, cancellationToken));
            var oracleSha256 = ComputeSha256(await File.ReadAllBytesAsync(oracleFile, cancellationToken));
            var sourceLength = sourceText.Length;
            var excerpt = sourceText;
            var citation = new ExternalGenerationCitation(question.QuestionId, sourceSha256, 0, sourceLength);
            var items = new[]
            {
                new ExternalGenerationContextItem(
                    question.QuestionId,
                    question.Category,
                    Path.GetRelativePath(outputRoot, sourceFile),
                    sourceSha256,
                    sourceSha256,
                    excerpt,
                    0,
                    sourceLength,
                    sourceLength,
                    1.0f)
            };

            var expectedClaims = guideline.Turns.Select(turn =>
                new ExternalExpectedGenerationClaim(
                    turn,
                    new[] { citation })).ToArray();

            cases.Add(new ExternalGenerationBenchmarkCase(
                question.QuestionId,
                question.QuestionId,
                question.Statement,
                Path.GetRelativePath(outputRoot, oracleFile),
                oracleSha256,
                false,
                items,
                expectedClaims));
        }

        var catalog = new ExternalGenerationBenchmarkCatalog(
            ExternalGenerationBenchmarkCatalog.SupportedFormatVersion,
            $"oab-bench-demo-{DateTime.UtcNow:yyyyMMdd}",
            cases);

        var catalogPath = Path.Combine(outputRoot, "catalog.json");
        var catalogJson = JsonSerializer.Serialize(catalog, JsonOptions);
        await File.WriteAllTextAsync(catalogPath, catalogJson, Utf8WithoutBom, cancellationToken);
        var catalogSha256 = ComputeSha256(await File.ReadAllBytesAsync(catalogPath, cancellationToken));

        var provenancePath = Path.Combine(outputRoot, "provenance.json");
        var provenance = new
        {
            corpusRoot = normalizedCorpusRoot,
            repositoryCommit,
            questionJsonlSha256 = ComputeSha256(await File.ReadAllBytesAsync(questionPath, cancellationToken)),
            guidelinesJsonlSha256 = ComputeSha256(await File.ReadAllBytesAsync(guidelinesPath, cancellationToken)),
            judgePromptsJsonlSha256 = File.Exists(judgePromptsPath)
                ? ComputeSha256(await File.ReadAllBytesAsync(judgePromptsPath, cancellationToken))
                : null,
            usedArtifacts = new[]
            {
                questionPath,
                guidelinesPath,
                judgePromptsPath
            }
        };
        await File.WriteAllTextAsync(provenancePath, JsonSerializer.Serialize(provenance, JsonOptions), Utf8WithoutBom, cancellationToken);

        return new OabBenchDemoCatalogInfo(
            catalogPath,
            catalogSha256,
            normalizedCorpusRoot,
            provenance.questionJsonlSha256,
            provenance.guidelinesJsonlSha256,
            provenance.judgePromptsJsonlSha256,
            repositoryCommit,
            provenance.usedArtifacts);
    }

    private static string ComputeSha256(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static OabQuestionRow ParseQuestionRow(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        return new OabQuestionRow(
            root.GetProperty("question_id").GetString() ?? throw new InvalidOperationException("question_id cannot be empty."),
            root.GetProperty("category").GetString() ?? throw new InvalidOperationException("category cannot be empty."),
            root.GetProperty("statement").GetString() ?? throw new InvalidOperationException("statement cannot be empty."));
    }

    private static OabGuidelineRow ParseGuidelineRow(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var turns = root.GetProperty("choices")[0].GetProperty("turns")
            .EnumerateArray()
            .Select(item => item.GetString() ?? string.Empty)
            .ToArray();

        return new OabGuidelineRow(
            root.GetProperty("question_id").GetString() ?? throw new InvalidOperationException("question_id cannot be empty."),
            turns);
    }

    private sealed record OabQuestionRow(string QuestionId, string Category, string Statement);

    private sealed record OabGuidelineRow(string QuestionId, IReadOnlyList<string> Turns);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };
}
