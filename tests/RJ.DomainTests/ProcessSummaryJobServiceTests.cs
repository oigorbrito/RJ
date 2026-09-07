using System.Globalization;
using RJ.Application.Generation;
using RJ.Application.Operations;
using RJ.Application.Security;
using RJ.Application.Sources;

namespace RJ.DomainTests;

public sealed class ProcessSummaryJobServiceTests
{
    private static readonly string RepoRoot = FindRepoRoot();
    private static readonly string FixturePath = Path.Combine(RepoRoot, "tests", "fixtures", "rj", "response_60031603620268160021_1.json");
    private static readonly DateTimeOffset ObservedAt =
        DateTimeOffset.Parse("2026-09-02T18:51:04.800Z", CultureInfo.InvariantCulture);
    private static readonly DateTimeOffset SubmittedAt =
        DateTimeOffset.Parse("2026-09-07T20:00:00.000Z", CultureInfo.InvariantCulture);
    private static readonly string[] RawSensitiveTokens = ["02727135971", "00439671914", "10172255000195"];

    [Fact]
    public async Task Submit_creates_validated_snapshot_bound_process_summary_job()
    {
        var telemetry = new InMemoryProcessSummaryTelemetry();
        var service = CreateService(telemetry);
        var source = Source(File.ReadAllText(FixturePath));

        var job = await service.SubmitAsync("idem-job-1", AuthorizedCaller(), source, "Resuma o processo.", CancellationToken.None);

        Assert.Equal("response_60031603620268160021_1", job.CaseId);
        Assert.Equal("6003160-36.2026.8.16.0021", job.Cnj);
        Assert.Equal(ProcessSummaryJobStatus.Validated, job.Status);
        Assert.Equal(ProcessSummaryPrompt.PromptVersion, job.SummaryVersion);
        Assert.Equal(0, job.RetrievalCalls);
        Assert.Equal(1, job.Attempts);
        Assert.False(job.Retried);
        Assert.Equal(SubmittedAt, job.CreatedAt);
        Assert.Equal(SubmittedAt, job.ValidatedAt);
        Assert.True(job.Validation.IsValid);
        Assert.Equal(source.RawContentSha256(), job.SnapshotSha256);
        Assert.Collection(
            job.History,
            submitted =>
            {
                Assert.Equal(ProcessSummaryJobStatus.Submitted, submitted.Status);
                Assert.Equal(SubmittedAt, submitted.ObservedAt);
                Assert.Equal("Job accepted for deterministic local processing.", submitted.Reason);
            },
            validated =>
            {
                Assert.Equal(ProcessSummaryJobStatus.Validated, validated.Status);
                Assert.Equal(SubmittedAt, validated.ObservedAt);
                Assert.Equal("Summary validated.", validated.Reason);
            });
        Assert.Same(job, service.GetJob(job.JobId));
        Assert.Same(job.Output, service.GetValidatedSummary(job.JobId));
        Assert.Collection(
            telemetry.Events,
            submitted => Assert.Equal(ProcessSummaryJobTelemetryStatus.Submitted, submitted.Status),
            validated =>
            {
                Assert.Equal(ProcessSummaryJobTelemetryStatus.Validated, validated.Status);
                Assert.Equal("1", validated.Tags["attempts"]);
                Assert.Equal("false", validated.Tags["retried"]);
            });
        Assert.All(telemetry.Events, AssertNoSensitiveTelemetry);
    }

    [Fact]
    public async Task Submit_is_idempotent_for_same_key_and_same_snapshot()
    {
        var telemetry = new InMemoryProcessSummaryTelemetry();
        var service = CreateService(telemetry);
        var source = Source(File.ReadAllText(FixturePath));

        var first = await service.SubmitAsync("idem-job-2", AuthorizedCaller(), source, "Resuma o processo.", CancellationToken.None);
        var second = await service.SubmitAsync("idem-job-2", AuthorizedCaller(), source, "Resuma o processo novamente.", CancellationToken.None);

        Assert.Same(first, second);
        Assert.Equal(first.JobId, second.JobId);
        Assert.Contains(telemetry.Events, item => item.Status == ProcessSummaryJobTelemetryStatus.IdempotentReplay);
    }

    [Fact]
    public async Task Submit_rejects_same_idempotency_key_with_different_snapshot()
    {
        var telemetry = new InMemoryProcessSummaryTelemetry();
        var service = CreateService(telemetry);
        await service.SubmitAsync("idem-job-3", AuthorizedCaller(), Source(File.ReadAllText(FixturePath)), "Resuma o processo.", CancellationToken.None);

        var changedSnapshot = File.ReadAllText(FixturePath)
            .Replace("\"cached\":false", "\"cached\":true", StringComparison.Ordinal);
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.SubmitAsync("idem-job-3", AuthorizedCaller(), Source(changedSnapshot), "Resuma o processo.", CancellationToken.None));

        Assert.Equal("Idempotency key is already bound to a different process snapshot.", exception.Message);
        Assert.Contains(telemetry.Events, item => item.Status == ProcessSummaryJobTelemetryStatus.IdempotencyConflict);
    }

    [Fact]
    public async Task Submit_rejects_caller_without_case_authorization()
    {
        var telemetry = new InMemoryProcessSummaryTelemetry();
        var service = CreateService(telemetry);
        var source = Source(File.ReadAllText(FixturePath));
        var caller = new CallerContext("tenant-1", "subject-1", ["other-case"], false);

        var exception = await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.SubmitAsync("idem-job-4", caller, source, "Resuma o processo.", CancellationToken.None));

        Assert.Equal("Caller is not authorized for this legal case.", exception.Message);
        Assert.Contains(telemetry.Events, item => item.Status == ProcessSummaryJobTelemetryStatus.Forbidden);
    }

    [Fact]
    public void Default_retention_policy_is_explicit_and_bounded()
    {
        var policy = ProcessSecurityPolicy.DefaultRetentionPolicy();

        Assert.Equal("rjudi-process-summary-v1", policy.PolicyId);
        Assert.Equal(TimeSpan.FromDays(90), policy.SummaryTtl);
        Assert.Equal(TimeSpan.FromDays(30), policy.EvidenceTtl);
    }

    [Fact]
    public async Task Freshness_policy_marks_job_fresh_stale_or_expired_deterministically()
    {
        var service = CreateService();
        var source = Source(File.ReadAllText(FixturePath));
        var job = await service.SubmitAsync("idem-job-5", AuthorizedCaller(), source, "Resuma o processo.", CancellationToken.None);
        var retention = ProcessSecurityPolicy.DefaultRetentionPolicy();

        var fresh = ProcessSummaryFreshnessPolicy.Evaluate(
            job,
            job.SnapshotSha256,
            ProcessSummaryPrompt.PromptVersion,
            SubmittedAt.AddDays(1),
            retention);
        var staleSnapshot = ProcessSummaryFreshnessPolicy.Evaluate(
            job,
            new string('a', 64),
            ProcessSummaryPrompt.PromptVersion,
            SubmittedAt.AddDays(1),
            retention);
        var staleVersion = ProcessSummaryFreshnessPolicy.Evaluate(
            job,
            job.SnapshotSha256,
            "rjudi-process-summary-v2",
            SubmittedAt.AddDays(1),
            retention);
        var expired = ProcessSummaryFreshnessPolicy.Evaluate(
            job,
            job.SnapshotSha256,
            ProcessSummaryPrompt.PromptVersion,
            SubmittedAt.AddDays(91),
            retention);

        Assert.Equal(ProcessSummaryFreshnessStatus.Fresh, fresh.Status);
        Assert.Equal(ProcessSummaryFreshnessStatus.Stale, staleSnapshot.Status);
        Assert.Equal("Snapshot hash changed.", staleSnapshot.Reason);
        Assert.Equal(ProcessSummaryFreshnessStatus.Stale, staleVersion.Status);
        Assert.Equal("Summary version changed.", staleVersion.Reason);
        Assert.Equal(ProcessSummaryFreshnessStatus.Expired, expired.Status);
        Assert.Equal("Summary retention window expired.", expired.Reason);
    }

    [Fact]
    public async Task Refresh_plan_for_existing_job_is_deterministic_and_read_only()
    {
        var service = CreateService();
        var source = Source(File.ReadAllText(FixturePath));
        var job = await service.SubmitAsync("idem-job-6", AuthorizedCaller(), source, "Resuma o processo.", CancellationToken.None);

        var fresh = service.GetRefreshPlan(job.JobId, job.SnapshotSha256, job.SummaryVersion);
        var stale = service.GetRefreshPlan(job.JobId, new string('b', 64), job.SummaryVersion);
        var missing = service.GetRefreshPlan("missing", job.SnapshotSha256, job.SummaryVersion);

        Assert.NotNull(fresh);
        Assert.Equal(ProcessSummaryRefreshAction.None, fresh.Action);
        Assert.False(fresh.RequiresScheduler);
        Assert.Equal("Summary is fresh.", fresh.Reason);
        Assert.NotNull(stale);
        Assert.Equal(ProcessSummaryRefreshAction.Refresh, stale.Action);
        Assert.True(stale.RequiresScheduler);
        Assert.Equal("Snapshot hash changed.", stale.Reason);
        Assert.Null(missing);
    }

    [Fact]
    public async Task Submit_admits_request_attachment_content_only_when_bound_to_observed_metadata()
    {
        var service = CreateService();
        var source = Source(File.ReadAllText(FixturePath));
        var legalCase = new JuditProcessSourceAdapter().Canonicalize(source);
        var content = new ProcessAttachmentContent(
            legalCase.Id.Value,
            legalCase.Attachments[0].Id,
            "attachment-extractor",
            "attachments/411788364428621657023616086781.html",
            "ATO ORDINATORIO OBSERVADO",
            SubmittedAt);

        var job = await service.SubmitAsync(
            "idem-job-7",
            AuthorizedCaller(),
            source,
            "Resuma o processo.",
            [content],
            CancellationToken.None);

        Assert.Equal(ProcessSummaryJobStatus.Validated, job.Status);
        Assert.True(job.Validation.IsValid);
        Assert.DoesNotContain(job.Output.Claims, claim => claim.Text.Contains("ATO ORDINATORIO OBSERVADO", StringComparison.Ordinal));

        var unknownAttachment = new ProcessAttachmentContent(
            legalCase.Id.Value,
            "unknown-attachment",
            "attachment-extractor",
            "attachments/unknown.html",
            "ATO ORDINATORIO OBSERVADO",
            SubmittedAt);
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.SubmitAsync(
                "idem-job-8",
                AuthorizedCaller(),
                source,
                "Resuma o processo.",
                [unknownAttachment],
                CancellationToken.None));

        Assert.Equal("Attachment content must reference observed attachment metadata.", exception.Message);
    }

    private static ProcessSourceDocument Source(string rawContent) =>
        new(
            JuditProcessSourceAdapter.JuditSourceSystem,
            "Judit",
            "tests/fixtures/rj/response_60031603620268160021_1.json",
            rawContent,
            ObservedAt);

    private static CallerContext AuthorizedCaller() =>
        new("tenant-1", "subject-1", ["response_60031603620268160021_1"], false);

    private static ProcessSummaryJobService CreateService(InMemoryProcessSummaryTelemetry? telemetry = null)
    {
        var canonicalization = new ProcessSourceCanonicalizationService(new[] { new JuditProcessSourceAdapter() });
        var composer = new ProcessGenerationContextComposer(new GenerationContextBuilder());
        var generation = new GenerationService(new DeterministicProcessSummaryModel());
        return new ProcessSummaryJobService(
            canonicalization,
            composer,
            generation,
            new FixedClock(SubmittedAt),
            telemetry ?? new InMemoryProcessSummaryTelemetry(),
            new EmptyProcessAttachmentContentStore());
    }

    private static void AssertNoSensitiveTelemetry(ProcessSummaryTelemetryEvent telemetryEvent)
    {
        var values = new[]
        {
            telemetryEvent.EventName,
            telemetryEvent.JobId ?? string.Empty,
            telemetryEvent.CaseId ?? string.Empty,
            telemetryEvent.Cnj ?? string.Empty,
            telemetryEvent.SnapshotSha256 ?? string.Empty,
            telemetryEvent.SummaryVersion ?? string.Empty
        }.Concat(telemetryEvent.Tags.SelectMany(item => new[] { item.Key, item.Value }));

        foreach (var value in values)
        {
            Assert.DoesNotContain("raw", value, StringComparison.OrdinalIgnoreCase);
            foreach (var token in RawSensitiveTokens)
            {
                Assert.DoesNotContain(token, value, StringComparison.Ordinal);
            }
        }
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : IProcessSummaryClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }

    private static string FindRepoRoot()
    {
        var current = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(current, "RJ.slnx")))
        {
            var parent = Directory.GetParent(current) ?? throw new InvalidOperationException("Repository root not found.");
            current = parent.FullName;
        }

        return current;
    }
}
