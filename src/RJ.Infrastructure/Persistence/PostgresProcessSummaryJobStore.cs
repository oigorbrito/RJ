using System.Text.Json;
using Npgsql;
using RJ.Application.Generation;

namespace RJ.Infrastructure.Persistence;

public sealed class PostgresProcessSummaryJobStore(NpgsqlDataSource dataSource) : IProcessSummaryJobStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<ProcessSummaryJob?> GetByJobIdAsync(
        string jobId,
        CancellationToken cancellationToken)
    {
        var id = Require(jobId, nameof(jobId));
        await using var command = dataSource.CreateCommand("""
            SELECT job_json::text
            FROM process_summary_jobs
            WHERE job_id = $1;
            """);
        command.Parameters.AddWithValue(id);
        return Deserialize(await command.ExecuteScalarAsync(cancellationToken));
    }

    public async Task<ProcessSummaryJob?> GetByScopedIdempotencyKeyAsync(
        string scopedIdempotencyKey,
        CancellationToken cancellationToken)
    {
        var key = Require(scopedIdempotencyKey, nameof(scopedIdempotencyKey));
        await using var command = dataSource.CreateCommand("""
            SELECT job_json::text
            FROM process_summary_jobs
            WHERE scoped_idempotency_key = $1;
            """);
        command.Parameters.AddWithValue(key);
        return Deserialize(await command.ExecuteScalarAsync(cancellationToken));
    }

    public async Task<IReadOnlyList<ProcessSummaryJob>> ListForMaintenanceAsync(
        string currentSummaryVersion,
        DateTimeOffset expiredBefore,
        int limit,
        CancellationToken cancellationToken)
    {
        var version = Require(currentSummaryVersion, nameof(currentSummaryVersion));
        if (limit <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(limit), limit, "Maintenance limit must be positive.");
        }

        await using var command = dataSource.CreateCommand("""
            SELECT job_json::text
            FROM process_summary_jobs
            WHERE updated_at < $1
               OR job_json ->> 'summaryVersion' <> $2
            ORDER BY updated_at ASC, job_id ASC
            LIMIT $3;
            """);
        command.Parameters.AddWithValue(expiredBefore);
        command.Parameters.AddWithValue(version);
        command.Parameters.AddWithValue(limit);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var jobs = new List<ProcessSummaryJob>();
        while (await reader.ReadAsync(cancellationToken))
        {
            jobs.Add(Deserialize(reader.GetString(0))
                ?? throw new InvalidOperationException("Stored process-summary job payload could not be deserialized."));
        }

        return jobs;
    }

    public async Task<ProcessSummaryJobStoreWriteResult> TryCreateAsync(
        ProcessSummaryJobStoreEntry entry,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var scopedKey = Require(entry.ScopedIdempotencyKey, nameof(entry.ScopedIdempotencyKey));
        var tenantHash = RequireSha256(entry.TenantIdHash, nameof(entry.TenantIdHash));
        var subjectHash = RequireSha256(entry.SubjectIdHash, nameof(entry.SubjectIdHash));
        var job = entry.Job ?? throw new ArgumentNullException(nameof(entry.Job));
        var snapshotSha256 = RequireSha256(job.SnapshotSha256, nameof(job.SnapshotSha256));
        var jobJson = JsonSerializer.Serialize(job, JsonOptions);

        await using var command = dataSource.CreateCommand("""
            INSERT INTO process_summary_jobs (
                job_id,
                scoped_idempotency_key,
                tenant_id_hash,
                subject_id_hash,
                case_id,
                snapshot_sha256,
                job_json,
                created_at,
                updated_at)
            VALUES ($1, $2, $3, $4, $5, $6, $7::jsonb, $8, $9)
            ON CONFLICT DO NOTHING
            RETURNING job_json::text;
            """);
        command.Parameters.AddWithValue(Require(job.JobId, nameof(job.JobId)));
        command.Parameters.AddWithValue(scopedKey);
        command.Parameters.AddWithValue(tenantHash);
        command.Parameters.AddWithValue(subjectHash);
        command.Parameters.AddWithValue(Require(job.CaseId, nameof(job.CaseId)));
        command.Parameters.AddWithValue(snapshotSha256);
        command.Parameters.AddWithValue(jobJson);
        command.Parameters.AddWithValue(job.CreatedAt);
        command.Parameters.AddWithValue(job.ValidatedAt);

        var inserted = Deserialize(await command.ExecuteScalarAsync(cancellationToken));
        if (inserted is not null)
        {
            return new ProcessSummaryJobStoreWriteResult(true, inserted);
        }

        var existing = await GetByScopedIdempotencyKeyAsync(scopedKey, cancellationToken)
            ?? await GetByJobIdAsync(job.JobId, cancellationToken)
            ?? throw new InvalidOperationException("Persistent process-summary job conflict could not be resolved.");

        return new ProcessSummaryJobStoreWriteResult(false, existing);
    }

    private static ProcessSummaryJob? Deserialize(object? value)
    {
        if (value is null || value is DBNull)
        {
            return null;
        }

        return JsonSerializer.Deserialize<ProcessSummaryJob>(Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture)!, JsonOptions)
            ?? throw new InvalidOperationException("Stored process-summary job payload could not be deserialized.");
    }

    private static string Require(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Persistent process-summary value cannot be empty.", parameterName);
        }

        return value.Trim();
    }

    private static string RequireSha256(string value, string parameterName)
    {
        var normalized = Require(value, parameterName).ToLowerInvariant();
        if (normalized.Length != 64 || normalized.Any(character => !Uri.IsHexDigit(character)))
        {
            throw new ArgumentException("Persistent process-summary hash must be a SHA-256 value.", parameterName);
        }

        return normalized;
    }
}
