using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using RJ.Application.Sources;

if (args.Length > 0 && StringComparer.OrdinalIgnoreCase.Equals(args[0], "capture"))
{
    return await CaptureAsync(args);
}

return await VerifyAsync(args);

static async Task<int> VerifyAsync(string[] args)
{
    if (args.Length != 4)
    {
        Console.Error.WriteLine("Usage: RJ.DataJudFixtureVerifier <fixture.json> <expected-cnj> <source-reference> <observed-at-iso8601>");
        Console.Error.WriteLine("   or: RJ.DataJudFixtureVerifier capture <tribunal-alias> <cnj> <output.json>");
        return 2;
    }

    var fixturePath = Path.GetFullPath(args[0]);
    if (!File.Exists(fixturePath))
    {
        Console.Error.WriteLine($"Fixture not found: {fixturePath}");
        return 2;
    }

    if (!DateTimeOffset.TryParse(
            args[3],
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.RoundtripKind,
            out var observedAt))
    {
        Console.Error.WriteLine("observed-at must be an ISO-8601 timestamp with offset.");
        return 2;
    }

    try
    {
        var raw = await File.ReadAllTextAsync(fixturePath);
        var observation = Parse(raw, args[1], args[2], observedAt);
        WriteObservation(observation);
        return 0;
    }
    catch (Exception exception) when (IsContractFailure(exception))
    {
        Console.Error.WriteLine($"DataJud fixture verification failed: {exception.Message}");
        return 3;
    }
}

static async Task<int> CaptureAsync(string[] args)
{
    if (args.Length != 4)
    {
        Console.Error.WriteLine("Usage: RJ.DataJudFixtureVerifier capture <tribunal-alias> <cnj> <output.json>");
        return 2;
    }

    var apiKey = Environment.GetEnvironmentVariable("RJ_DATAJUD_API_KEY");
    if (string.IsNullOrWhiteSpace(apiKey))
    {
        Console.Error.WriteLine("RJ_DATAJUD_API_KEY is required for capture mode.");
        return 2;
    }

    try
    {
        var endpoint = DataJudPublicApiContract.SearchEndpoint(args[1]);
        var query = DataJudPublicApiContract.BuildProcessNumberQuery(args[2]);
        using var client = new HttpClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("APIKey", apiKey.Trim());
        request.Content = new StringContent(query, Encoding.UTF8, "application/json");

        using var response = await client.SendAsync(request);
        var raw = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
        {
            Console.Error.WriteLine($"DataJud capture failed with HTTP {(int)response.StatusCode} ({response.StatusCode}).");
            return 4;
        }

        var observedAt = DateTimeOffset.UtcNow;
        var observation = Parse(raw, args[2], endpoint.AbsoluteUri, observedAt);
        var outputPath = Path.GetFullPath(args[3]);
        var outputDirectory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(outputDirectory))
        {
            Directory.CreateDirectory(outputDirectory);
        }
        await File.WriteAllTextAsync(outputPath, raw);

        var metadata = new DataJudCaptureMetadata(
            endpoint.AbsoluteUri,
            observation.Cnj,
            observedAt,
            observation.SourceSha256,
            outputPath);
        var metadataPath = outputPath + ".metadata.json";
        await File.WriteAllTextAsync(
            metadataPath,
            JsonSerializer.Serialize(metadata, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));

        Console.WriteLine($"Captured DataJud response: {outputPath}");
        Console.WriteLine($"Metadata: {metadataPath}");
        WriteObservation(observation);
        return 0;
    }
    catch (HttpRequestException exception)
    {
        Console.Error.WriteLine($"DataJud capture network failure: {exception.Message}");
        return 5;
    }
    catch (Exception exception) when (IsContractFailure(exception))
    {
        Console.Error.WriteLine($"DataJud capture contract failure: {exception.Message}");
        return 3;
    }
}

static DataJudProcessObservation Parse(
    string raw,
    string expectedCnj,
    string sourceReference,
    DateTimeOffset observedAt)
{
    var source = new ProcessSourceDocument(
        DataJudPublicApiContract.SourceSystem,
        "CNJ DataJud public API",
        sourceReference,
        raw,
        observedAt);
    return DataJudPublicApiContract.ParseSingleProcessResponse(source, expectedCnj);
}

static void WriteObservation(DataJudProcessObservation observation) =>
    Console.WriteLine(JsonSerializer.Serialize(observation, new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    }));

static bool IsContractFailure(Exception exception) =>
    exception is ArgumentException or InvalidOperationException or JsonException or FormatException;

internal sealed record DataJudCaptureMetadata(
    string SourceReference,
    string Cnj,
    DateTimeOffset ObservedAt,
    string SourceSha256,
    string FixturePath);
