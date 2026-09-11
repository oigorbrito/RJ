namespace RJ.Application.Generation;

public interface IProcessSummaryJobStore
{
    Task<ProcessSummaryJob?> GetByJobIdAsync(
        string jobId,
        CancellationToken cancellationToken);

    Task<ProcessSummaryJob?> GetByScopedIdempotencyKeyAsync(
        string scopedIdempotencyKey,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<ProcessSummaryJob>> ListForMaintenanceAsync(
        string currentSummaryVersion,
        DateTimeOffset expiredBefore,
        int limit,
        CancellationToken cancellationToken);

    Task<ProcessSummaryJobStoreWriteResult> TryCreateAsync(
        ProcessSummaryJobStoreEntry entry,
        CancellationToken cancellationToken);
}

public sealed record ProcessSummaryJobStoreEntry(
    string ScopedIdempotencyKey,
    string TenantIdHash,
    string SubjectIdHash,
    ProcessSummaryJob Job);

public sealed record ProcessSummaryJobStoreWriteResult(
    bool Created,
    ProcessSummaryJob Job);

public sealed class NoopProcessSummaryJobStore : IProcessSummaryJobStore
{
    public Task<ProcessSummaryJob?> GetByJobIdAsync(
        string jobId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<ProcessSummaryJob?>(null);
    }

    public Task<ProcessSummaryJob?> GetByScopedIdempotencyKeyAsync(
        string scopedIdempotencyKey,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<ProcessSummaryJob?>(null);
    }

    public Task<IReadOnlyList<ProcessSummaryJob>> ListForMaintenanceAsync(
        string currentSummaryVersion,
        DateTimeOffset expiredBefore,
        int limit,
        CancellationToken cancellationToken)
    {
        Require(currentSummaryVersion, nameof(currentSummaryVersion));
        RequirePositive(limit, nameof(limit));
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<ProcessSummaryJob>>([]);
    }

    public Task<ProcessSummaryJobStoreWriteResult> TryCreateAsync(
        ProcessSummaryJobStoreEntry entry,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new ProcessSummaryJobStoreWriteResult(true, entry.Job));
    }

    private static string Require(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Process-summary store value cannot be empty.", parameterName);
        }

        return value.Trim();
    }

    private static void RequirePositive(int value, string parameterName)
    {
        if (value <= 0)
        {
            throw new ArgumentOutOfRangeException(parameterName, value, "Value must be positive.");
        }
    }
}

public sealed class InMemoryProcessSummaryJobStore : IProcessSummaryJobStore
{
    private readonly Dictionary<string, ProcessSummaryJobStoreEntry> byJobId = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ProcessSummaryJobStoreEntry> byScopedKey = new(StringComparer.Ordinal);
    private readonly object sync = new();

    public Task<ProcessSummaryJob?> GetByJobIdAsync(
        string jobId,
        CancellationToken cancellationToken)
    {
        var id = Require(jobId, nameof(jobId));
        cancellationToken.ThrowIfCancellationRequested();
        lock (sync)
        {
            return Task.FromResult(byJobId.TryGetValue(id, out var entry) ? entry.Job : null);
        }
    }

    public Task<ProcessSummaryJob?> GetByScopedIdempotencyKeyAsync(
        string scopedIdempotencyKey,
        CancellationToken cancellationToken)
    {
        var key = Require(scopedIdempotencyKey, nameof(scopedIdempotencyKey));
        cancellationToken.ThrowIfCancellationRequested();
        lock (sync)
        {
            return Task.FromResult(byScopedKey.TryGetValue(key, out var entry) ? entry.Job : null);
        }
    }

    public Task<IReadOnlyList<ProcessSummaryJob>> ListForMaintenanceAsync(
        string currentSummaryVersion,
        DateTimeOffset expiredBefore,
        int limit,
        CancellationToken cancellationToken)
    {
        var version = Require(currentSummaryVersion, nameof(currentSummaryVersion));
        RequirePositive(limit, nameof(limit));
        cancellationToken.ThrowIfCancellationRequested();
        lock (sync)
        {
            IReadOnlyList<ProcessSummaryJob> jobs = byJobId.Values
                .Select(entry => entry.Job)
                .Where(job => job.ValidatedAt < expiredBefore
                    || !StringComparer.Ordinal.Equals(job.SummaryVersion, version))
                .OrderBy(job => job.ValidatedAt)
                .ThenBy(job => job.JobId, StringComparer.Ordinal)
                .Take(limit)
                .ToArray();
            return Task.FromResult(jobs);
        }
    }

    public Task<ProcessSummaryJobStoreWriteResult> TryCreateAsync(
        ProcessSummaryJobStoreEntry entry,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);
        cancellationToken.ThrowIfCancellationRequested();
        var key = Require(entry.ScopedIdempotencyKey, nameof(entry.ScopedIdempotencyKey));
        var jobId = Require(entry.Job.JobId, nameof(entry.Job.JobId));

        lock (sync)
        {
            if (byScopedKey.TryGetValue(key, out var existing))
            {
                return Task.FromResult(new ProcessSummaryJobStoreWriteResult(false, existing.Job));
            }

            if (byJobId.TryGetValue(jobId, out existing))
            {
                return Task.FromResult(new ProcessSummaryJobStoreWriteResult(false, existing.Job));
            }

            byScopedKey.Add(key, entry);
            byJobId.Add(jobId, entry);
            return Task.FromResult(new ProcessSummaryJobStoreWriteResult(true, entry.Job));
        }
    }

    private static string Require(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Process-summary store value cannot be empty.", parameterName);
        }

        return value.Trim();
    }

    private static void RequirePositive(int value, string parameterName)
    {
        if (value <= 0)
        {
            throw new ArgumentOutOfRangeException(parameterName, value, "Value must be positive.");
        }
    }
}
