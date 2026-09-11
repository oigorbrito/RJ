namespace RJ.Application.Generation;

public interface IProcessSummaryJobStore
{
    ProcessSummaryJob? GetByIdempotencyKey(string scopedKey);

    ProcessSummaryJob? GetById(string jobId);

    bool TryAdd(string scopedKey, ProcessSummaryJob job);
}

public interface IProcessSummaryJobRecovery
{
    Task<IReadOnlyList<ProcessSummaryJob>> RecoverIncompleteAsync(CancellationToken cancellationToken);
}

public sealed class InMemoryProcessSummaryJobStore : IProcessSummaryJobStore, IProcessSummaryJobRecovery
{
    private readonly Dictionary<string, ProcessSummaryJob> jobsByIdempotencyKey = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ProcessSummaryJob> jobsById = new(StringComparer.Ordinal);
    private readonly object sync = new();

    public ProcessSummaryJob? GetByIdempotencyKey(string scopedKey)
    {
        lock (sync)
        {
            return jobsByIdempotencyKey.GetValueOrDefault(scopedKey);
        }
    }

    public ProcessSummaryJob? GetById(string jobId)
    {
        lock (sync)
        {
            return jobsById.GetValueOrDefault(jobId);
        }
    }

    public bool TryAdd(string scopedKey, ProcessSummaryJob job)
    {
        ArgumentNullException.ThrowIfNull(job);
        ValidatePublishable(job);
        lock (sync)
        {
            if (jobsByIdempotencyKey.ContainsKey(scopedKey) || jobsById.ContainsKey(job.JobId))
            {
                return false;
            }

            jobsByIdempotencyKey.Add(scopedKey, job);
            jobsById.Add(job.JobId, job);
            return true;
        }
    }

    private static void ValidatePublishable(ProcessSummaryJob job)
    {
        if (string.IsNullOrWhiteSpace(job.JobId)
            || string.IsNullOrWhiteSpace(job.IdempotencyKey)
            || string.IsNullOrWhiteSpace(job.CaseId)
            || string.IsNullOrWhiteSpace(job.Cnj)
            || string.IsNullOrWhiteSpace(job.SnapshotSha256)
            || string.IsNullOrWhiteSpace(job.SummaryVersion))
        {
            throw new ArgumentException("A process summary job must have complete identity fields.", nameof(job));
        }

        if (job.Status is ProcessSummaryJobStatus.Submitted
            || job.History.Count != 2
            || job.History[0].Status != ProcessSummaryJobStatus.Submitted
            || job.History[1].Status != job.Status
            || (job.Status == ProcessSummaryJobStatus.Validated) != job.Validation.IsValid)
        {
            throw new ArgumentException("A process summary job must be published in a terminal valid state.", nameof(job));
        }
    }

    public Task<IReadOnlyList<ProcessSummaryJob>> RecoverIncompleteAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Process-local storage has no state to recover after a process restart.
        return Task.FromResult<IReadOnlyList<ProcessSummaryJob>>([]);
    }
}
