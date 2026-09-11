using System.Text.Json;
using RJ.Application.Sources;

if (args.Length != 4)
{
    Console.Error.WriteLine("Usage: RJ.DataJudFixtureVerifier <fixture.json> <expected-cnj> <source-reference> <observed-at-iso8601>");
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
    var source = new ProcessSourceDocument(
        DataJudPublicApiContract.SourceSystem,
        "CNJ DataJud public API",
        args[2],
        raw,
        observedAt);
    var observation = DataJudPublicApiContract.ParseSingleProcessResponse(source, args[1]);
    Console.WriteLine(JsonSerializer.Serialize(observation, new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    }));
    return 0;
}
catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or JsonException or FormatException)
{
    Console.Error.WriteLine($"DataJud fixture verification failed: {exception.Message}");
    return 3;
}
