using System.Globalization;
using RJ.Application.Generation;
using RJ.Infrastructure.Persistence;

namespace RJ.IntegrationTests;

public sealed class PostgresProcessSummaryMaintenanceTests
{
    [Fact]
    public async Task Maintenance_listing_filters_actionable_jobs_and_orders_oldest_first()
    {
        await using var database = await TemporaryPostgresDatabase.CreateAsync();
        await PostgresSchema.MigrateAsync(database.DataSource);
        var store = new PostgresProcessSummaryJobStore(database.DataSource);
        var olderExpired = Job(
            "job-expired",
            ProcessSummaryPrompt.PromptVersion,
            DateTimeOffset.Parse("2026-01-01T00:00:00Z", CultureInfo.InvariantCulture));
        var staleVersion = Job(
            "job-stale",
            "old-version",
            DateTimeOffset.Parse("2026-02-01T00:00:00Z", CultureInfo.InvariantCulture));
        var fresh = Job(
            "job-fresh",
            ProcessSummaryPrompt.PromptVersion,
            DateTimeOffset.Parse("2026-04-01T00:00:00Z", CultureInfo.InvariantCulture));
        var expiredBefore = DateTimeOffset.Parse("2026-03-01T00:00:00Z", CultureInfo.InvariantCulture);

        await store.TryCreateAsync(Entry(fresh), CancellationToken.None);
        await store.TryCreateAsync(Entry(staleVersion), CancellationToken.None);
        await store.TryCreateAsync(Entry(olderExpired), CancellationToken.None);

        var first = await store.ListForMaintenanceAsync(
            ProcessSummaryPrompt.PromptVersion,
            expiredBefore,
            1,
            CancellationToken.None);
        var candidates = await store.ListForMaintenanceAsync(
            ProcessSummaryPrompt.PromptVersion,
            expiredBefore,
            10,
            CancellationToken.None);

        Assert.Equal("job-expired", Assert.Single(first).JobId);
        Assert.Equal(["job-expired", "job-stale"], candidates.Select(job => job.JobId).ToArray());
        Assert.DoesNotContain(candidates, job => job.JobId == "job-fresh");
    }

    private static ProcessSummaryJobStoreEntry Entry(ProcessSummaryJob job) =>
        new(
            $"scope-{job.JobId}",
            new string('a', 64),
            new string('b', 64),
            job);

    private static ProcessSummaryJob Job(
        string jobId,
        string summaryVersion,
        DateTimeOffset validatedAt)
    {
        var output = new GenerationModelOutput(
            false,
            null,
            [new GenerationClaim(
                "claim",
                [new GenerationCitation("doc-1", new string('c', 64), 0, 4)])]);
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
}
