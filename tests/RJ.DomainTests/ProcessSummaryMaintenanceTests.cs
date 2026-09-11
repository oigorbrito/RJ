using RJ.Application.Generation;
using RJ.Application.Operations;
using RJ.Application.Security;

namespace RJ.DomainTests;

public sealed class ProcessSummaryMaintenanceTests
{
    [Fact]
    public async Task Fresh_job_is_evaluated_without_dispatch()
    {
        var now = DateTimeOffset.Parse("2026-09-11T00:00:00Z", System.Globalization.CultureInfo.InvariantCulture);
        var store = new InMemoryProcessSummaryJobStore();
        await AddAsync(store, Job("job-fresh", ProcessSummaryPrompt.PromptVersion, now.AddDays(-1)));
        var dispatcher = new RecordingDispatcher();
        var telemetry = new InMemoryProcessSummaryTelemetry();
        var service = new ProcessSummaryMaintenanceService(store, new FixedClock(now), dispatcher, telemetry);

        var result = await service.RunOnceAsync(10, CancellationToken.None);

        Assert.Equal(1, result.EvaluatedCount);
        Assert.Equal(0, result.DispatchedCount);
        Assert.Empty(dispatcher.Items);
    }

    [Fact]
    public async Task Previous_summary_version_dispatches_refresh_with_stable_work_id()
    {
        var now = DateTimeOffset.Parse("2026-09-11T00:00:00Z", System.Globalization.CultureInfo.InvariantCulture);
        var store = new InMemoryProcessSummaryJobStore();
        var job = Job("job-stale", "rjudi-process-summary-previous", now.AddDays(-1));
        await AddAsync(store, job);
        var firstDispatcher = new RecordingDispatcher();
        var first = new ProcessSummaryMaintenanceService(
            store,
            new FixedClock(now),
            firstDispatcher,
            new InMemoryProcessSummaryTelemetry());

        await first.RunOnceAsync(10, CancellationToken.None);

        var secondDispatcher = new RecordingDispatcher();
        var second = new ProcessSummaryMaintenanceService(
            store,
            new FixedClock(now.AddMinutes(1)),
            secondDispatcher,
            new InMemoryProcessSummaryTelemetry());
        await second.RunOnceAsync(10, CancellationToken.None);

        var firstItem = Assert.Single(firstDispatcher.Items);
        var secondItem = Assert.Single(secondDispatcher.Items);
        Assert.Equal(ProcessSummaryRefreshAction.Refresh, firstItem.Action);
        Assert.Equal(firstItem.WorkId, secondItem.WorkId);
        Assert.Equal(64, firstItem.WorkId.Length);
    }

    [Fact]
    public async Task Expired_job_dispatches_rebuild_and_validated_summary_is_not_published()
    {
        var now = DateTimeOffset.Parse("2026-09-11T00:00:00Z", System.Globalization.CultureInfo.InvariantCulture);
        var retention = ProcessSecurityPolicy.DefaultRetentionPolicy();
        var expiredJob = Job(
            "job-expired",
            ProcessSummaryPrompt.PromptVersion,
            now - retention.SummaryTtl - TimeSpan.FromSeconds(1));
        var store = new InMemoryProcessSummaryJobStore();
        await AddAsync(store, expiredJob);
        var dispatcher = new RecordingDispatcher();
        var telemetry = new InMemoryProcessSummaryTelemetry();
        var clock = new FixedClock(now);
        var maintenance = new ProcessSummaryMaintenanceService(store, clock, dispatcher, telemetry);
        var coordinator = new ProcessSummaryPersistenceCoordinator(store, clock, telemetry);

        var run = await maintenance.RunOnceAsync(10, CancellationToken.None);
        var published = await coordinator.GetValidatedSummaryAsync(expiredJob.JobId, CancellationToken.None);

        var workItem = Assert.Single(run.WorkItems);
        Assert.Equal(ProcessSummaryRefreshAction.Rebuild, workItem.Action);
        Assert.Null(published);
        Assert.Contains(
            telemetry.Events,
            item => StringComparer.Ordinal.Equals(item.EventName, "process_summary.retention_blocked_publication"));
    }

    [Fact]
    public async Task Maintenance_batch_uses_deterministic_oldest_first_order()
    {
        var now = DateTimeOffset.Parse("2026-09-11T00:00:00Z", System.Globalization.CultureInfo.InvariantCulture);
        var store = new InMemoryProcessSummaryJobStore();
        await AddAsync(store, Job("job-newer", "old-version", now.AddDays(-2)));
        await AddAsync(store, Job("job-older", "old-version", now.AddDays(-3)));
        var dispatcher = new RecordingDispatcher();
        var service = new ProcessSummaryMaintenanceService(
            store,
            new FixedClock(now),
            dispatcher,
            new InMemoryProcessSummaryTelemetry());

        var result = await service.RunOnceAsync(1, CancellationToken.None);

        Assert.Equal(1, result.EvaluatedCount);
        Assert.Equal("job-older", Assert.Single(result.WorkItems).JobId);
    }

    [Fact]
    public async Task Invalid_batch_size_is_rejected_before_store_access()
    {
        var service = new ProcessSummaryMaintenanceService(
            new InMemoryProcessSummaryJobStore(),
            new FixedClock(DateTimeOffset.UtcNow),
            new RecordingDispatcher(),
            new InMemoryProcessSummaryTelemetry());

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => service.RunOnceAsync(0, CancellationToken.None));
    }

    private static async Task AddAsync(InMemoryProcessSummaryJobStore store, ProcessSummaryJob job)
    {
        var result = await store.TryCreateAsync(
            new ProcessSummaryJobStoreEntry(
                $"scope-{job.JobId}",
                new string('a', 64),
                new string('b', 64),
                job),
            CancellationToken.None);
        Assert.True(result.Created);
    }

    private static ProcessSummaryJob Job(
        string jobId,
        string summaryVersion,
        DateTimeOffset validatedAt)
    {
        var citation = new GenerationCitation("doc-1", new string('c', 64), 0, 4);
        var output = new GenerationModelOutput(
            false,
            null,
            [new GenerationClaim("claim", [citation])]);
        return new ProcessSummaryJob(
            jobId,
            $"idem-{jobId}",
            "case-1",
            "6003160-36.2026.8.16.0021",
            new string('d', 64),
            summaryVersion,
            0,
            1,
            false,
            validatedAt.AddMinutes(-1),
            validatedAt,
            ProcessSummaryJobStatus.Validated,
            output,
            new ProcessSummaryValidationResult(true, []),
            [new ProcessSummaryJobEvent(ProcessSummaryJobStatus.Validated, validatedAt, "validated")]);
    }

    private sealed class FixedClock(DateTimeOffset now) : IProcessSummaryClock
    {
        public DateTimeOffset UtcNow { get; } = now;
    }

    private sealed class RecordingDispatcher : IProcessSummaryRefreshDispatcher
    {
        public List<ProcessSummaryMaintenanceWorkItem> Items { get; } = [];

        public Task DispatchAsync(
            ProcessSummaryMaintenanceWorkItem workItem,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Items.Add(workItem);
            return Task.CompletedTask;
        }
    }
}
