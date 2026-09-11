using System.Globalization;
using RJ.Application.Generation;
using RJ.Application.Operations;
using RJ.Application.Security;

namespace RJ.DomainTests;

public sealed class ProcessSummaryPersistenceCoordinatorTests
{
    private static readonly DateTimeOffset ObservedAt = DateTimeOffset.Parse(
        "2026-09-11T00:00:00Z",
        CultureInfo.InvariantCulture);

    [Fact]
    public async Task Coordinator_replays_persisted_job_without_regeneration()
    {
        var store = new InMemoryProcessSummaryJobStore();
        var telemetry = new InMemoryProcessSummaryTelemetry();
        var coordinator = new ProcessSummaryPersistenceCoordinator(store, new FixedClock(ObservedAt), telemetry);
        var caller = Caller();
        var job = Job(new string('a', 64));

        var persisted = await coordinator.PersistSubmissionAsync(
            caller,
            "idem-1",
            job,
            CancellationToken.None);
        var replay = await coordinator.ResolveExistingSubmissionAsync(
            caller,
            "idem-1",
            job.SnapshotSha256,
            CancellationToken.None);

        Assert.Equal(job, persisted);
        Assert.Equal(job, replay);
        Assert.Contains(telemetry.Events, item => item.EventName == "process_summary.persisted");
        Assert.Contains(telemetry.Events, item => item.EventName == "process_summary.persisted_idempotent_replay");
    }

    [Fact]
    public async Task Coordinator_rejects_same_scoped_key_for_different_snapshot()
    {
        var coordinator = new ProcessSummaryPersistenceCoordinator(
            new InMemoryProcessSummaryJobStore(),
            new FixedClock(ObservedAt),
            new NoopProcessSummaryTelemetry());
        var caller = Caller();
        await coordinator.PersistSubmissionAsync(
            caller,
            "idem-1",
            Job(new string('a', 64)),
            CancellationToken.None);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => coordinator.ResolveExistingSubmissionAsync(
                caller,
                "idem-1",
                new string('b', 64),
                CancellationToken.None));

        Assert.Equal(
            "Idempotency key is already bound to a different process snapshot.",
            exception.Message);
    }

    [Fact]
    public async Task Coordinator_recovers_validated_summary_and_refresh_plan_from_store()
    {
        var store = new InMemoryProcessSummaryJobStore();
        var coordinator = new ProcessSummaryPersistenceCoordinator(
            store,
            new FixedClock(ObservedAt.AddMinutes(5)),
            new NoopProcessSummaryTelemetry());
        var caller = Caller();
        var job = Job(new string('a', 64));
        await coordinator.PersistSubmissionAsync(caller, "idem-1", job, CancellationToken.None);

        var recovered = await coordinator.GetJobAsync(job.JobId, CancellationToken.None);
        var output = await coordinator.GetValidatedSummaryAsync(job.JobId, CancellationToken.None);
        var plan = await coordinator.GetRefreshPlanAsync(
            job.JobId,
            job.SnapshotSha256,
            job.SummaryVersion,
            CancellationToken.None);

        Assert.Equal(job, recovered);
        Assert.Equal(job.Output, output);
        Assert.NotNull(plan);
        Assert.False(plan.RequiresScheduler);
    }

    [Fact]
    public async Task Persistence_identity_scopes_same_key_by_tenant_and_subject()
    {
        var first = ProcessSummaryPersistenceIdentity.ScopedIdempotencyKey(
            Caller("tenant-1", "subject-1"),
            "same-key");
        var second = ProcessSummaryPersistenceIdentity.ScopedIdempotencyKey(
            Caller("tenant-1", "subject-2"),
            "same-key");
        var third = ProcessSummaryPersistenceIdentity.ScopedIdempotencyKey(
            Caller("tenant-2", "subject-1"),
            "same-key");

        Assert.NotEqual(first, second);
        Assert.NotEqual(first, third);
        Assert.Equal(194, first.Length);
        await Task.CompletedTask;
    }

    private static CallerContext Caller(
        string tenantId = "tenant-1",
        string subjectId = "subject-1") =>
        new(tenantId, subjectId, ["case-1"], false, ["Judit"]);

    private static ProcessSummaryJob Job(string snapshotSha256)
    {
        var output = new GenerationModelOutput(
            false,
            null,
            [
                new GenerationClaim(
                    "test",
                    [new GenerationCitation("document-1", new string('d', 64), 0, 4)])
            ]);
        return new ProcessSummaryJob(
            "job-1",
            "idem-1",
            "case-1",
            "6003160-36.2026.8.16.0021",
            snapshotSha256,
            ProcessSummaryPrompt.PromptVersion,
            0,
            1,
            false,
            ObservedAt,
            ObservedAt,
            ProcessSummaryJobStatus.Validated,
            output,
            new ProcessSummaryValidationResult(true, []),
            [
                new ProcessSummaryJobEvent(ProcessSummaryJobStatus.Submitted, ObservedAt, "submitted"),
                new ProcessSummaryJobEvent(ProcessSummaryJobStatus.Validated, ObservedAt, "validated")
            ]);
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : IProcessSummaryClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }
}
