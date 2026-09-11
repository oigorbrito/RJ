using RJ.Application.Generation;
using RJ.Application.Security;

namespace RJ.Application.Operations;

public interface IProcessSummaryRefreshDispatcher
{
    Task DispatchAsync(
        ProcessSummaryMaintenanceWorkItem workItem,
        CancellationToken cancellationToken);
}

public sealed record ProcessSummaryMaintenanceWorkItem(
    string JobId,
    string CaseId,
    string SnapshotSha256,
    string SummaryVersion,
    ProcessSummaryRefreshAction Action,
    string Reason,
    DateTimeOffset ObservedAt);

public sealed class ProcessSummaryMaintenanceService(
    IProcessSummaryJobStore store,
    IProcessSummaryClock clock,
    IProcessSummaryRefreshDispatcher dispatcher,
    IProcessSummaryTelemetry telemetry)
{
    public async Task<ProcessSummaryMaintenanceRunResult> RunOnceAsync(
        int batchSize,
        CancellationToken cancellationToken)
    {
        if (batchSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(batchSize), batchSize, "Maintenance batch size must be positive.");
        }

        var now = clock.UtcNow;
        var jobs = await store.ListForMaintenanceAsync(batchSize, cancellationToken);
        var dispatched = new List<ProcessSummaryMaintenanceWorkItem>();

        foreach (var job in jobs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var freshness = ProcessSummaryFreshnessPolicy.Evaluate(
                job,
                job.SnapshotSha256,
                ProcessSummaryPrompt.PromptVersion,
                now,
                ProcessSecurityPolicy.DefaultRetentionPolicy());
            var plan = ProcessSummaryRefreshPlanner.Plan(freshness);
            if (!plan.RequiresScheduler)
            {
                continue;
            }

            var workItem = new ProcessSummaryMaintenanceWorkItem(
                job.JobId,
                job.CaseId,
                job.SnapshotSha256,
                job.SummaryVersion,
                plan.Action,
                plan.Reason,
                now);
            await dispatcher.DispatchAsync(workItem, cancellationToken);
            dispatched.Add(workItem);

            telemetry.Record(new ProcessSummaryTelemetryEvent(
                "process_summary.maintenance_dispatch",
                job.JobId,
                job.CaseId,
                job.Cnj,
                job.SnapshotSha256,
                job.SummaryVersion,
                ProcessSummaryJobTelemetryStatus.RefreshPlan,
                now,
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["action"] = plan.Action.ToString(),
                    ["freshness"] = freshness.Status.ToString(),
                    ["dispatcher"] = dispatcher.GetType().Name
                }));
        }

        return new ProcessSummaryMaintenanceRunResult(
            jobs.Count,
            dispatched.Count,
            dispatched);
    }
}

public sealed record ProcessSummaryMaintenanceRunResult(
    int EvaluatedCount,
    int DispatchedCount,
    IReadOnlyList<ProcessSummaryMaintenanceWorkItem> WorkItems);

public sealed class NoopProcessSummaryRefreshDispatcher : IProcessSummaryRefreshDispatcher
{
    public Task DispatchAsync(
        ProcessSummaryMaintenanceWorkItem workItem,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(workItem);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }
}
