using Npgsql;
using RJ.Application.Generation;
using RJ.Application.Security;

namespace RJ.Infrastructure.Persistence;

public sealed class PostgresProcessSummaryJobAccessStore(NpgsqlDataSource dataSource) : IProcessSummaryJobAccessStore
{
    public async Task<bool> TryBindAsync(
        string jobId,
        CallerContext caller,
        string caseId,
        CancellationToken cancellationToken)
    {
        var scope = await ReadScopeAsync(jobId, cancellationToken);
        if (scope is null)
        {
            return false;
        }

        return Matches(scope, caller, caseId);
    }

    public async Task<bool> CanAccessAsync(
        string jobId,
        CallerContext caller,
        CancellationToken cancellationToken)
    {
        var scope = await ReadScopeAsync(jobId, cancellationToken);
        if (scope is null)
        {
            return false;
        }

        return StringComparer.Ordinal.Equals(scope.TenantIdHash, ProcessSummaryPersistenceIdentity.TenantIdHash(caller))
            && StringComparer.Ordinal.Equals(scope.SubjectIdHash, ProcessSummaryPersistenceIdentity.SubjectIdHash(caller))
            && caller.AuthorizedCaseIds.Contains(scope.CaseId, StringComparer.Ordinal);
    }

    private async Task<PersistedScope?> ReadScopeAsync(
        string jobId,
        CancellationToken cancellationToken)
    {
        var id = Require(jobId, nameof(jobId));
        await using var command = dataSource.CreateCommand("""
            SELECT tenant_id_hash, subject_id_hash, case_id
            FROM process_summary_jobs
            WHERE job_id = $1;
            """);
        command.Parameters.AddWithValue(id);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new PersistedScope(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2));
    }

    private static bool Matches(PersistedScope scope, CallerContext caller, string caseId)
    {
        ArgumentNullException.ThrowIfNull(caller);
        var normalizedCaseId = Require(caseId, nameof(caseId));
        return StringComparer.Ordinal.Equals(scope.TenantIdHash, ProcessSummaryPersistenceIdentity.TenantIdHash(caller))
            && StringComparer.Ordinal.Equals(scope.SubjectIdHash, ProcessSummaryPersistenceIdentity.SubjectIdHash(caller))
            && StringComparer.Ordinal.Equals(scope.CaseId, normalizedCaseId)
            && caller.AuthorizedCaseIds.Contains(normalizedCaseId, StringComparer.Ordinal);
    }

    private static string Require(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Persistent job-access value cannot be empty.", parameterName);
        }

        return value.Trim();
    }

    private sealed record PersistedScope(
        string TenantIdHash,
        string SubjectIdHash,
        string CaseId);
}
