using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using RJ.Application.Benchmarking;

namespace RJ.BenchmarkCli;

public sealed record RulingBrDemoCatalogInfo(
    string CatalogPath,
    string CatalogSha256,
    string CorpusRoot,
    string CorpusArtifactSha256,
    string? RepositoryCommit,
    string[] UsedArtifactPaths);

public static class RulingBrDemoCatalogAdapter
{
    public const string EnvironmentVariable = "RJ_LEGAL_DEMO_RETRIEVAL_CORPUS";
    private const string SampleFileName = "sample-5.json";
    private const string CorpusArchiveFileName = "rulingbr-v1.2.tar.xz";
    private const string CorpusExtractedFileName = "rulingbr-v1.2.jsonl";
    private static readonly UTF8Encoding Utf8WithoutBom = new(false);

    public static async Task<RulingBrDemoCatalogInfo> BuildAsync(
        string corpusRoot,
        string? repositoryCommit,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(corpusRoot))
        {
            throw new ArgumentException("Corpus root cannot be empty.", nameof(corpusRoot));
        }

        var normalizedCorpusRoot = Path.GetFullPath(corpusRoot);
        var corpusPath = await ResolveCorpusPathAsync(normalizedCorpusRoot, cancellationToken);
        var cases = await ReadCasesAsync(corpusPath, cancellationToken);

        if (cases.Length == 0)
        {
            throw new InvalidOperationException("RulingBR demo corpus must contain at least one sample case.");
        }

        var outputRoot = Path.Combine(Path.GetTempPath(), $"rj-rulingbr-adapter-{Guid.NewGuid():N}");
        Directory.CreateDirectory(outputRoot);
        var sourceRoot = Path.Combine(outputRoot, "source");
        var oracleRoot = Path.Combine(outputRoot, "oracle");
        Directory.CreateDirectory(sourceRoot);
        Directory.CreateDirectory(oracleRoot);

        var externalCases = new List<ExternalGenerationBenchmarkCase>(cases.Length);
        foreach (var demoCase in cases)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var sourceFile = Path.Combine(sourceRoot, $"{demoCase.Id}.txt");
            var oracleFile = Path.Combine(oracleRoot, $"{demoCase.Id}.txt");
            await File.WriteAllTextAsync(sourceFile, demoCase.SourceText, Utf8WithoutBom, cancellationToken);
            await File.WriteAllTextAsync(oracleFile, demoCase.OracleText, Utf8WithoutBom, cancellationToken);

            var sourceBytes = await File.ReadAllBytesAsync(sourceFile, cancellationToken);
            var oracleBytes = await File.ReadAllBytesAsync(oracleFile, cancellationToken);
            var sourceSha256 = ComputeSha256(sourceBytes);
            var oracleSha256 = ComputeSha256(oracleBytes);
            var sourceLength = demoCase.SourceText.Length;

            externalCases.Add(new ExternalGenerationBenchmarkCase(
                demoCase.Id,
                demoCase.Id,
                demoCase.SourceText,
                Path.GetRelativePath(outputRoot, oracleFile),
                oracleSha256,
                false,
                [
                    new ExternalGenerationContextItem(
                        demoCase.Id,
                        demoCase.Area,
                        Path.GetRelativePath(outputRoot, sourceFile),
                        sourceSha256,
                        sourceSha256,
                        demoCase.SourceText,
                        0,
                        sourceLength,
                        sourceLength,
                        1.0f)
                ],
                [
                    new ExternalExpectedGenerationClaim(
                        demoCase.OracleText,
                        [new ExternalGenerationCitation(demoCase.Id, sourceSha256, 0, sourceLength)])
                ]));
        }

        var catalog = new ExternalGenerationBenchmarkCatalog(
            ExternalGenerationBenchmarkCatalog.SupportedFormatVersion,
            $"rulingbr-demo-{DateTime.UtcNow:yyyyMMdd}",
            externalCases);

        var catalogPath = Path.Combine(outputRoot, "catalog.json");
        var catalogJson = JsonSerializer.Serialize(catalog, JsonOptions);
        await File.WriteAllTextAsync(catalogPath, catalogJson, Utf8WithoutBom, cancellationToken);
        var catalogSha256 = ComputeSha256(await File.ReadAllBytesAsync(catalogPath, cancellationToken));

        var provenancePath = Path.Combine(outputRoot, "provenance.json");
        var provenance = new
        {
            corpusRoot = normalizedCorpusRoot,
            repositoryCommit,
            sampleFileSha256 = ComputeSha256(await File.ReadAllBytesAsync(corpusPath, cancellationToken)),
            usedArtifacts = new[] { corpusPath }
        };
        await File.WriteAllTextAsync(provenancePath, JsonSerializer.Serialize(provenance, JsonOptions), Utf8WithoutBom, cancellationToken);

        return new RulingBrDemoCatalogInfo(
            catalogPath,
            catalogSha256,
            normalizedCorpusRoot,
            provenance.sampleFileSha256,
            repositoryCommit,
            provenance.usedArtifacts);
    }

    public static async Task<string> ResolveCorpusPathAsync(string corpusRoot, CancellationToken cancellationToken)
    {
        var normalizedCorpusRoot = Path.GetFullPath(corpusRoot);
        var extracted = Path.Combine(normalizedCorpusRoot, CorpusExtractedFileName);
        if (File.Exists(extracted))
        {
            return extracted;
        }

        var archivePath = Path.Combine(normalizedCorpusRoot, CorpusArchiveFileName);
        if (File.Exists(archivePath))
        {
            var extractionRoot = Path.Combine(Path.GetTempPath(), $"rj-rulingbr-extract-{Guid.NewGuid():N}");
            Directory.CreateDirectory(extractionRoot);
            var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "tar",
                ArgumentList = { "-xf", archivePath, "-C", extractionRoot },
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }) ?? throw new InvalidOperationException("Failed to start tar extraction for RulingBR corpus.");
            await process.WaitForExitAsync(cancellationToken);
            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException("RulingBR corpus extraction failed.");
            }

            var resolved = Path.Combine(extractionRoot, CorpusExtractedFileName);
            if (!File.Exists(resolved))
            {
                throw new FileNotFoundException("Extracted RulingBR corpus file was not found.", resolved);
            }

            return resolved;
        }

        var samplePath = Path.Combine(normalizedCorpusRoot, SampleFileName);
        if (File.Exists(samplePath))
        {
            return samplePath;
        }

        throw new FileNotFoundException("RulingBR corpus archive or sample file was not found.", archivePath);
    }

    public static Task<RulingBrDemoCase[]> ReadCasesFromFileAsync(
        string samplePath,
        CancellationToken cancellationToken) =>
        ReadCasesAsync(samplePath, cancellationToken);

    private static async Task<RulingBrDemoCase[]> ReadCasesAsync(string samplePath, CancellationToken cancellationToken)
    {
        var text = await File.ReadAllTextAsync(samplePath, cancellationToken);
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new InvalidOperationException("RulingBR sample file cannot be empty.");
        }

        var trimmed = text.TrimStart('\uFEFF', '\u200B', '\t', '\r', '\n', ' ');
        var rows = trimmed.StartsWith('[')
            ? ReadArray(trimmed)
            : trimmed.StartsWith('{')
                ? ReadSingleOrConcatenated(trimmed)
                : ReadJsonLines(trimmed);

        return rows.Select((row, index) => BuildCase(row, index + 1)).ToArray();
    }

    private static IEnumerable<RulingBrSampleRow> ReadArray(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException("RulingBR sample file is not a JSON array.");
        }

        foreach (var element in document.RootElement.EnumerateArray())
        {
            yield return ParseSampleRow(element);
        }
    }

    private static List<RulingBrSampleRow> ReadSingleOrConcatenated(string json)
    {
        var bytes = Encoding.UTF8.GetBytes(json);
        var rows = new List<RulingBrSampleRow>();
        var offset = 0;
        while (offset < bytes.Length)
        {
            while (offset < bytes.Length && char.IsWhiteSpace((char)bytes[offset]))
            {
                offset++;
            }

            if (offset >= bytes.Length)
            {
                break;
            }

            var reader = new Utf8JsonReader(bytes.AsSpan(offset), isFinalBlock: true, state: default);
            if (!reader.Read())
            {
                break;
            }

            using var document = JsonDocument.ParseValue(ref reader);
            rows.Add(ParseSampleRow(document.RootElement));
            offset += (int)reader.BytesConsumed;
        }

        return rows;
    }

    private static IEnumerable<RulingBrSampleRow> ReadJsonLines(string text)
    {
        var lines = text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var line in lines)
        {
            if (!line.StartsWith('{'))
            {
                throw new InvalidOperationException("RulingBR sample file contains non-JSON content.");
            }

            using var document = JsonDocument.Parse(line);
            yield return ParseSampleRow(document.RootElement);
        }
    }

    private static RulingBrSampleRow ParseSampleRow(JsonElement root)
    {
        return new RulingBrSampleRow(
            root.GetProperty("ementa").GetString() ?? throw new InvalidOperationException("ementa cannot be empty."),
            root.GetProperty("acordao").GetString() ?? throw new InvalidOperationException("acordao cannot be empty."),
            root.TryGetProperty("area", out var area) ? area.GetString() ?? "desconhecida" : "desconhecida",
            root.TryGetProperty("relator", out var relator) ? relator.GetString() ?? "desconhecido" : "desconhecido");
    }

    private static RulingBrDemoCase BuildCase(RulingBrSampleRow row, int index)
    {
        var id = $"rulingbr-{index:000}";
        return new RulingBrDemoCase(
            id,
            row.Area,
            row.Ementa.Trim(),
            row.Acordao.Trim());
    }

    private static string ComputeSha256(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private sealed record RulingBrSampleRow(string Ementa, string Acordao, string Area, string Relator);

    public sealed record RulingBrDemoCase(string Id, string Area, string SourceText, string OracleText);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };
}
