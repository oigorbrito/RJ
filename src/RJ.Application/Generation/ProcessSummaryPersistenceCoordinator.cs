using RJ.Application.Operations;
using RJ.Application.Security;

namespace RJ.Application.Generation;

public sealed class ProcessSummaryPersistenceCoordinator(
    IProcessSummaryJobStore store,
    IProcessSummaryClock clock,
    IProcessSummaryTelemetry telemetry)
{
    public async Task<ProcessSummaryJob?> ResolveExistingSubmissionAsync(
        CallerContext caller,
        string idempotencyKey,
        string snapshotSha256,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(caller);
        var scopedKey = ProcessSummaryPersistenceIdentity.ScopedIdempotencyKey(caller, idempotencyKey);
        var snapshot = RequireSha256(snapshotSha256, nameof(snapshotSha256));
        var existing = await store.GetByScopedIdempotencyKeyAsync(scopedKey, cancellationToken);
        if (existing is null)
        {
            return null;
        }

        if (!StringComparer.Ordinal.Equals(existing.SnapshotSha256, snapshot))
        {
            throw new InvalidOperationException("Idempotency key is already bound to a different process snapshot.");
        }

        telemetry.Record(Event(
            "process_summary.persisted_idempotent_replay",
            ProcessSummaryJobTelemetryStatus.IdempotentReplay,
            existing,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["persistence"] = "postgres"
            }));
        return existing;
    }

    public async Task<ProcessSummaryJob> PersistSubmissionAsync(
        CallerContext caller,
        string idempotencyKey,
        ProcessSummaryJob job,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(job);
        var entry = new ProcessSummaryJobStoreEntry(
            ProcessSummaryPersistenceIdentity.ScopedIdempotencyKey(caller, idempotencyKey),
            ProcessSummaryPersistenceIdentity.TenantIdHash(caller),
            ProcessSummaryPersistenceIdentity.SubjectIdHash(caller),
            job);
        var result = await store.TryCreateAsync(entry, cancellationToken);

        if (!StringComparer.Ordinal.Equals(result.Job.SnapshotSha256, job.SnapshotSha256))
        {
            throw new InvalidOperationException("Idempotency key is already bound to a different process snapshot.");
        }

        telemetry.Record(Event(
            result.Created ? "process_summary.persisted" : "process_summary.persistence_race_replay",
            result.Created ? ToTelemetryStatus(result.Job.Status) : ProcessSummaryJobTelemetryStatus.IdempotentReplay,
            result.Job,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["persistence"] = "postgres",
                ["created"] = result.Created.ToString(System.Globalization.CultureInfo.InvariantCulture).ToLowerInvariant()
            }));
        return result.Job;
    }

    public Task<ProcessSummaryJob?> GetJobAsync(
        string jobId,
        CancellationToken cancellationToken) =>
        store.GetByJobIdAsync(Require(jobId, nameof(jobId)), cancellationToken);

    public async Task<GenerationModelOutput?> GetValidatedSummaryAsync(
        string jobId,
        CancellationToken cancellationToken)
    {
        var job = await GetJobAsync(jobId, cancellationToken);
        return job is { Status: ProcessSummaryJobStatus.Validated, Validation.IsValid: true }
            ? job.Output
            : null;
    }

    public async Task<ProcessSummaryRefreshPlan?> GetRefreshPlanAsync(
        string jobId,
        string currentSnapshotSha256,
        string currentSummaryVersion,
        CancellationToken cancellationToken)
    {
        var snapshotSha256 = RequireSha256(currentSnapshotSha256, nameof(currentSnapshotSha256));
        var summaryVersion = Require(currentSummaryVersion, nameof(currentSummaryVersion));
        var job = await GetJobAsync(jobId, cancellationToken);
        if (job is null)
        {
            return null;
        }

        var freshness = ProcessSummaryFreshnessPolicy.Evaluate(
            job,
            snapshotSha256,
            summaryVersion,
            clock.UtcNow,
            ProcessSecurityPolicy.DefaultRetentionPolicy());
        var plan = ProcessSummaryRefreshPlanner.Plan(freshness);
        telemetry.Record(Event(
            "process_summary.persisted_refresh_plan",
            ProcessSummaryJobTelemetryStatus.RefreshPlan,
            job,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["decision"] = freshness.Status.ToString(),
                ["action"] = plan.Action.ToString(),
                ["requires_scheduler"] = plan.RequiresScheduler.ToString(System.Globalization.CultureInfo.InvariantCulture).ToLowerInvariant(),
                ["persistence"] = "postgres"
            }));
        return plan;
    }

    private ProcessSummaryTelemetryEvent Event(
        string eventName,
        ProcessSummaryJobTelemetryStatus status,
        ProcessSummaryJob job,
        IReadOnlyDictionary<string, string> tags) =>
        new(
            eventName,
            job.JobId,
            job.CaseId,
            job.Cnj,
            job.SnapshotSha256,
            job.SummaryVersion,
            status,
            clock.UtcNow,
            tags);

    private static ProcessSummaryJobTelemetryStatus ToTelemetryStatus(ProcessSummaryJobStatus status) =>
        status switch
        {
            ProcessSummaryJobStatus.Validated => ProcessSummaryJobTelemetryStatus.Validated,
            ProcessSummaryJobStatus.Failed => ProcessSummaryJobTelemetryStatus.Failed,
            _ => ProcessSummaryJobTelemetryStatus.Submitted
        };

    private static string Require(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Process-summary persistence value cannot be empty.", parameterName);
        }

        return value.Trim();
    }

    private static string RequireSha256(string value, string parameterName)
    {
        var normalized = Require(value, parameterName).ToLowerInvariant();
        if (normalized.Length != 64 || normalized.Any(character => !Uri.IsHexDigit(character)))
        {
            throw new ArgumentException("SHA-256 value must contain exactly 64 hexadecimal characters.", parameterName);
        }

        return normalized;
    }
}
