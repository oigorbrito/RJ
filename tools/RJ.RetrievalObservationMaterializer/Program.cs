using System.Text.Json;
using RJ.Application.Benchmarking;

if (args.Length != 8)
{
    Console.Error.WriteLine("Usage: RJ.RetrievalObservationMaterializer <retrieval-report.json> <expected-report-sha256> <source-report-reference> <recorded-at> <policy.json> <expected-policy-sha256> <policy-reference> <output-dir>");
    return 2;
}

var reportPath = Path.GetFullPath(args[0]);
var expectedReportSha = args[1].Trim().ToLowerInvariant();
var sourceReportReference = args[2].Trim();
var recordedAtText = args[3].Trim();
var policyPath = Path.GetFullPath(args[4]);
var expectedPolicySha = args[5].Trim().ToLowerInvariant();
var policyReference = args[6].Trim();
var outputDir = Path.GetFullPath(args[7]);
var policyJsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

try
{
    if (!File.Exists(reportPath) || !File.Exists(policyPath))
    {
        Console.Error.WriteLine("Retrieval report or materialization policy is unavailable.");
        return 2;
    }

    if (!DateTimeOffset.TryParse(recordedAtText, out var recordedAt) || recordedAt == default)
    {
        Console.Error.WriteLine("recorded-at must be a valid non-default timestamp.");
        return 2;
    }

    var reportBytes = await File.ReadAllBytesAsync(reportPath);
    VerifySha(reportBytes, expectedReportSha, "retrieval report");
    var report = RetrievalBenchmarkJson.Parse(reportBytes);

    var policyBytes = await File.ReadAllBytesAsync(policyPath);
    VerifySha(policyBytes, expectedPolicySha, "materialization policy");
    var policy = JsonSerializer.Deserialize<RetrievalEmpiricalObservationPolicy>(policyBytes, policyJsonOptions)
        ?? throw new InvalidOperationException("Retrieval materialization policy produced no document.");
    policy.Validate().RequireMatches(report);
    RequireSafeFileToken(policy.TreatmentId, "treatment-id");

    var materialized = new RetrievalEmpiricalObservationMaterializer().Materialize(
        report,
        sourceReportReference,
        expectedReportSha,
        policyReference,
        expectedPolicySha,
        recordedAt,
        policy);

    Directory.CreateDirectory(outputDir);
    var index = new List<object>(materialized.Count);
    foreach (var item in materialized)
    {
        RequireSafeFileToken(item.CaseId, "case-id");
        var fileName = $"{item.CaseId}.{item.TreatmentId}.empirical-observation.json";
        var path = Path.Combine(outputDir, fileName);
        await File.WriteAllBytesAsync(path, item.ArtifactUtf8Json);
        index.Add(new
        {
            item.CaseId,
            item.TreatmentId,
            artifactReference = fileName,
            item.ArtifactSha256,
            sourceArtifactReference = item.Artifact.SourceArtifactReference,
            sourceArtifactSha256 = item.Artifact.SourceArtifactSha256,
            materializationPolicyReference = item.Artifact.MaterializationPolicyReference,
            materializationPolicySha256 = item.Artifact.MaterializationPolicySha256
        });
    }

    var indexBytes = JsonSerializer.SerializeToUtf8Bytes(index, new JsonSerializerOptions
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    });
    var indexPath = Path.Combine(outputDir, $"{policy.TreatmentId}.empirical-observation-index.json");
    await File.WriteAllBytesAsync(indexPath, indexBytes);
    Console.WriteLine(JsonSerializer.Serialize(new
    {
        treatmentId = policy.TreatmentId,
        implementationId = policy.ImplementationId,
        observationCount = materialized.Count,
        indexPath,
        indexSha256 = EmpiricalSelectionManifest.ComputeSha256(indexBytes)
    }));
    return 0;
}
catch (JsonException exception)
{
    Console.Error.WriteLine(exception.Message);
    return 3;
}
catch (InvalidOperationException exception)
{
    Console.Error.WriteLine(exception.Message);
    return 3;
}
catch (ArgumentException exception)
{
    Console.Error.WriteLine(exception.Message);
    return 3;
}
catch (IOException exception)
{
    Console.Error.WriteLine(exception.Message);
    return 3;
}
catch (UnauthorizedAccessException exception)
{
    Console.Error.WriteLine(exception.Message);
    return 3;
}

static void VerifySha(ReadOnlySpan<byte> bytes, string expected, string label)
{
    var actual = EmpiricalSelectionManifest.ComputeSha256(bytes);
    if (!StringComparer.Ordinal.Equals(actual, expected))
    {
        throw new InvalidOperationException($"{label} SHA-256 mismatch: expected {expected}, observed {actual}.");
    }
}

static void RequireSafeFileToken(string value, string label)
{
    if (string.IsNullOrWhiteSpace(value)
        || value.Contains("..", StringComparison.Ordinal)
        || value.Contains('/')
        || value.Contains('\\')
        || value.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
    {
        throw new ArgumentException($"{label} cannot contain path traversal or invalid filename characters.", label);
    }
}
