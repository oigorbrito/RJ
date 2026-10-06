using System.Text.Json;
using Npgsql;
using RJ.Domain.Cases;

namespace RJ.Infrastructure.Persistence;

public sealed class PostgresProcessCatalog(NpgsqlDataSource dataSource)
{
    private readonly NpgsqlDataSource _dataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));

    public async Task<ProcessCatalogEntry?> GetByCnjAsync(string cnj, CancellationToken cancellationToken)
    {
        var normalized = new LegalCaseCnj(cnj).Value;
        return await ReadSingleAsync(
            "SELECT case_id, cnj, source_name, canonical_json::text FROM process_cases WHERE cnj = @value LIMIT 1;",
            normalized,
            cancellationToken);
    }

    public async Task<ProcessCatalogEntry?> GetByCaseIdAsync(string caseId, CancellationToken cancellationToken)
    {
        var normalized = new LegalCaseId(caseId).Value;
        return await ReadSingleAsync(
            "SELECT case_id, cnj, source_name, canonical_json::text FROM process_cases WHERE case_id = @value LIMIT 1;",
            normalized,
            cancellationToken);
    }

    public async Task UpsertAsync(ProcessCatalogEntry entry, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);
        _ = new LegalCaseId(entry.CaseId);
        _ = new LegalCaseCnj(entry.Cnj);
        if (string.IsNullOrWhiteSpace(entry.SourceName))
        {
            throw new ArgumentException("Process source name cannot be empty.", nameof(entry));
        }

        using var json = JsonDocument.Parse(entry.CanonicalJson);
        if (json.RootElement.ValueKind != JsonValueKind.Object)
        {
            throw new ArgumentException("Process canonical JSON must be an object.", nameof(entry));
        }

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO process_cases (case_id, cnj, source_name, canonical_json, updated_at)
            VALUES (@case_id, @cnj, @source_name, CAST(@canonical_json AS jsonb), now())
            ON CONFLICT (case_id) DO UPDATE SET
                cnj = EXCLUDED.cnj,
                source_name = EXCLUDED.source_name,
                canonical_json = EXCLUDED.canonical_json,
                updated_at = now();
            """,
            connection)
        {
            CommandTimeout = PostgresCommandPolicy.CommandTimeoutSeconds
        };
        command.Parameters.AddWithValue("case_id", entry.CaseId);
        command.Parameters.AddWithValue("cnj", new LegalCaseCnj(entry.Cnj).Value);
        command.Parameters.AddWithValue("source_name", entry.SourceName.Trim());
        command.Parameters.AddWithValue("canonical_json", entry.CanonicalJson);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<ProcessCatalogEntry?> ReadSingleAsync(
        string sql,
        string value,
        CancellationToken cancellationToken)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection)
        {
            CommandTimeout = PostgresCommandPolicy.CommandTimeoutSeconds
        };
        command.Parameters.AddWithValue("value", value);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new ProcessCatalogEntry(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3));
    }
}

public sealed record ProcessCatalogEntry(
    string CaseId,
    string Cnj,
    string SourceName,
    string CanonicalJson);
