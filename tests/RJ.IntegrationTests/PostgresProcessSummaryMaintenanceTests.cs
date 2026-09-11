using RJ.Application.Generation;
using RJ.Infrastructure.Persistence;

namespace RJ.IntegrationTests;

public sealed class PostgresProcessSummaryMaintenanceTests
{
    [Fact]
    public async Task Maintenance_listing_is_deterministic_and_oldest_first()
    {
        await using var database = await TemporaryPostgresDatabase.CreateAsync();
        await PostgresSchema.MigrateAsync(database.DataSource);
        var store = new PostgresProcessSummaryJobStore(database.DataSource);
        var older = Job("job-older", DateTimeOffset.Parse("2026-01-01T00:00:00Z", System.Globalization.CultureInfo.InvariantCulture));
        var newer = Job("job-newer", DateTimeOffset.Parse("2026-02-01T00:00:00Z", System.Globalization.CultureInfo.InvariantCulture));

        await store.TryCreateAsync(Entry(newer), CancellationToken.None);
        await store.TryCreateAsync(Entry(older), CancellationToken.None);

        var first = await store.ListForMaintenanceAsync(1, CancellationToken.None);
        var both = await store.ListForMaintenanceAsync(2, CancellationToken.None);

        Assert.Equal("job-older", Assert.Single(first).JobId);
        Assert.Equal(["job-older", "job-newer"], both.Select(job => job.JobId).ToArray());
    }

    private static ProcessSummaryJobStoreEntry Entry(ProcessSummaryJob job) =>
        new(
            $"scope-{job.JobId}",
            new string('a', 64),
            new string('b', 64),
            job);

    private static ProcessSummaryJob Job(string jobId, DateTimeOffset validatedAt)
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
            ProcessSummaryPrompt.PromptVersion,
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
