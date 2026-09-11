using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Npgsql;
using RJ.Domain.Cases;
using RJ.Infrastructure.Persistence;

var jsonOptions = new JsonSerializerOptions
{
    PropertyNameCaseInsensitive = true,
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
};

var connectionString = Environment.GetEnvironmentVariable("RJ_POSTGRES_CONNECTION")
    ?? throw new InvalidOperationException("RJ_POSTGRES_CONNECTION is required.");

var dataPath = args.Length > 0
    ? Path.GetFullPath(args[0])
    : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "demo-data", "processes.json"));

if (!File.Exists(dataPath))
{
    throw new FileNotFoundException("Demo dataset was not found.", dataPath);
}

var json = await File.ReadAllTextAsync(dataPath);
var cases = JsonSerializer.Deserialize<List<DemoCase>>(json, jsonOptions)
    ?? throw new InvalidOperationException("Demo dataset is empty or invalid.");

await using var dataSource = NpgsqlDataSource.Create(connectionString);
await PostgresSchema.EnsureCurrentAsync(dataSource);
var catalog = new PostgresProcessCatalog(dataSource);

foreach (var item in cases)
{
    var cnj = new LegalCaseCnj(item.Cnj).Value;
    var caseId = new LegalCaseId(item.CaseId).Value;
    var canonicalJson = JsonSerializer.Serialize(item.Process, jsonOptions);

    await catalog.UpsertAsync(
        new ProcessCatalogEntry(caseId, cnj, item.SourceName, canonicalJson),
        CancellationToken.None);

    foreach (var document in item.Documents)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(document.Content))).ToLowerInvariant();
        await using var connection = await dataSource.OpenConnectionAsync();
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO legal_documents (
                case_id, document_id, source_name, raw_content, content, content_sha256)
            VALUES (
                @case_id, @document_id, @source_name, @raw_content, @content, @content_sha256)
            ON CONFLICT (case_id, document_id) DO UPDATE SET
                source_name = EXCLUDED.source_name,
                raw_content = EXCLUDED.raw_content,
                content = EXCLUDED.content,
                content_sha256 = EXCLUDED.content_sha256;
            """,
            connection);
        command.Parameters.AddWithValue("case_id", caseId);
        command.Parameters.AddWithValue("document_id", document.DocumentId);
        command.Parameters.AddWithValue("source_name", item.SourceName);
        command.Parameters.AddWithValue("raw_content", document.Content);
        command.Parameters.AddWithValue("content", document.Content);
        command.Parameters.AddWithValue("content_sha256", hash);
        await command.ExecuteNonQueryAsync();
    }

    Console.WriteLine($"SEEDED {cnj} -> {caseId}");
}

Console.WriteLine($"DEMO_SEED_COMPLETE count={cases.Count}");

public sealed record DemoCase(
    string CaseId,
    string Cnj,
    string SourceName,
    JsonElement Process,
    IReadOnlyList<DemoDocument> Documents);

public sealed record DemoDocument(string DocumentId, string Content);
