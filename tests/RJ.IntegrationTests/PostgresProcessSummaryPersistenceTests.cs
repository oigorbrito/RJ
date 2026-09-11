using RJ.Application.Generation;
using RJ.Application.Security;
using RJ.Infrastructure.Persistence;

namespace RJ.IntegrationTests;

public sealed class PostgresProcessSummaryPersistenceTests
{
    [Fact]
    public async Task Job_store_persists_and_recovers_terminal_job_across_store_instances()
    {
        await using var database = await TemporaryPostgresDatabase.CreateAsync();
        await PostgresSchema.MigrateAsync(database.DataSource);
        var firstStore = new PostgresProcessSummaryJobStore(database.DataSource);
        var caller = Caller("tenant-1", "subject-1", "case-1");
        var job = Job("job-1", new string('a', 64));
        var scopedKey = ProcessSummaryPersistenceIdentity.ScopedIdempotencyKey(caller, "idem-1");

        var written = await firstStore.TryCreateAsync(
            new ProcessSummaryJobStoreEntry(
                scopedKey,
                ProcessSummaryPersistenceIdentity.TenantIdHash(caller),
                ProcessSummaryPersistenceIdentity.SubjectIdHash(caller),
                job),
            CancellationToken.None);

        Assert.True(written.Created);
        Assert.Equal(job, written.Job);

        var secondStore = new PostgresProcessSummaryJobStore(database.DataSource);
        var byId = await secondStore.GetByJobIdAsync(job.JobId, CancellationToken.None);
        var byKey = await secondStore.GetByScopedIdempotencyKeyAsync(scopedKey, CancellationToken.None);

        Assert.Equal(job, byId);
        Assert.Equal(job, byKey);
    }

    [Fact]
    public async Task Job_store_returns_existing_job_for_scoped_idempotency_race()
    {
        await using var database = await TemporaryPostgresDatabase.CreateAsync();
        await PostgresSchema.MigrateAsync(database.DataSource);
        var store = new PostgresProcessSummaryJobStore(database.DataSource);
        var caller = Caller("tenant-1", "subject-1", "case-1");
        var scopedKey = ProcessSummaryPersistenceIdentity.ScopedIdempotencyKey(caller, "idem-1");
        var original = Job("job-1", new string('a', 64));
        var competing = Job("job-2", new string('b', 64));

        var first = await store.TryCreateAsync(Entry(scopedKey, caller, original), CancellationToken.None);
        var second = await store.TryCreateAsync(Entry(scopedKey, caller, competing), CancellationToken.None);

        Assert.True(first.Created);
        Assert.False(second.Created);
        Assert.Equal(original, second.Job);
    }

    [Fact]
    public async Task Access_store_enforces_persisted_tenant_subject_and_case_scope()
    {
        await using var database = await TemporaryPostgresDatabase.CreateAsync();
        await PostgresSchema.MigrateAsync(database.DataSource);
        var jobStore = new PostgresProcessSummaryJobStore(database.DataSource);
        var accessStore = new PostgresProcessSummaryJobAccessStore(database.DataSource);
        var owner = Caller("tenant-1", "subject-1", "case-1");
        var job = Job("job-1", new string('a', 64));
        var scopedKey = ProcessSummaryPersistenceIdentity.ScopedIdempotencyKey(owner, "idem-1");
        await jobStore.TryCreateAsync(Entry(scopedKey, owner, job), CancellationToken.None);

        Assert.True(await accessStore.TryBindAsync(job.JobId, owner, job.CaseId, CancellationToken.None));
        Assert.True(await accessStore.CanAccessAsync(job.JobId, owner, CancellationToken.None));
        Assert.False(await accessStore.CanAccessAsync(
            job.JobId,
            Caller("tenant-2", "subject-1", "case-1"),
            CancellationToken.None));
        Assert.False(await accessStore.CanAccessAsync(
            job.JobId,
            Caller("tenant-1", "subject-2", "case-1"),
            CancellationToken.None));
        Assert.False(await accessStore.CanAccessAsync(
            job.JobId,
            Caller("tenant-1", "subject-1", "case-2"),
            CancellationToken.None));
    }

    [Fact]
    public async Task Scoped_idempotency_isolated_by_caller_identity()
    {
        await using var database = await TemporaryPostgresDatabase.CreateAsync();
        await PostgresSchema.MigrateAsync(database.DataSource);
        var store = new PostgresProcessSummaryJobStore(database.DataSource);
        var firstCaller = Caller("tenant-1", "subject-1", "case-1");
        var secondCaller = Caller("tenant-1", "subject-2", "case-1");
        var firstKey = ProcessSummaryPersistenceIdentity.ScopedIdempotencyKey(firstCaller, "same-key");
        var secondKey = ProcessSummaryPersistenceIdentity.ScopedIdempotencyKey(secondCaller, "same-key");

        var first = await store.TryCreateAsync(Entry(firstKey, firstCaller, Job("job-1", new string('a', 64))), CancellationToken.None);
        var second = await store.TryCreateAsync(Entry(secondKey, secondCaller, Job("job-2", new string('b', 64))), CancellationToken.None);

        Assert.True(first.Created);
        Assert.True(second.Created);
        Assert.NotEqual(firstKey, secondKey);
    }

    private static ProcessSummaryJobStoreEntry Entry(
        string scopedKey,
        CallerContext caller,
        ProcessSummaryJob job) =>
        new(
            scopedKey,
            ProcessSummaryPersistenceIdentity.TenantIdHash(caller),
            ProcessSummaryPersistenceIdentity.SubjectIdHash(caller),
            job);

    private static CallerContext Caller(string tenantId, string subjectId, string caseId) =>
        new(tenantId, subjectId, [caseId], false, ["Judit"]);

    private static ProcessSummaryJob Job(string jobId, string snapshotSha256)
    {
        var observedAt = DateTimeOffset.Parse(
            "2026-09-11T00:00:00Z",
            System.Globalization.CultureInfo.InvariantCulture);
        var citation = new GenerationCitation("document-1", new string('d', 64), 0, 4);
        var output = new GenerationModelOutput(
            false,
            null,
            [new GenerationClaim("test", [citation])]);
        var validation = new ProcessSummaryValidationResult(true, []);
        return new ProcessSummaryJob(
            jobId,
            "idem-1",
            "case-1",
            "6003160-36.2026.8.16.0021",
            snapshotSha256,
            ProcessSummaryPrompt.PromptVersion,
            0,
            1,
            false,
            observedAt,
            observedAt,
            ProcessSummaryJobStatus.Validated,
            output,
            validation,
            [
                new ProcessSummaryJobEvent(ProcessSummaryJobStatus.Submitted, observedAt, "submitted"),
                new ProcessSummaryJobEvent(ProcessSummaryJobStatus.Validated, observedAt, "validated")
            ]);
    }
}
