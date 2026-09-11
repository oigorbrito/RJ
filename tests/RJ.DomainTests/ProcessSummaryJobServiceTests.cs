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
            submitted =>
            {
                Assert.Equal(ProcessSummaryJobTelemetryStatus.Submitted, submitted.Status);
                Assert.Equal("0", submitted.Tags["retrieval_calls"]);
            },
            validated =>
            {
                Assert.Equal(ProcessSummaryJobTelemetryStatus.Validated, validated.Status);
                Assert.Equal("1", validated.Tags["attempts"]);
                Assert.Equal("0", validated.Tags["duration_ms"]);
                Assert.Equal("false", validated.Tags["retried"]);
                Assert.Equal("valid", validated.Tags["validator_status"]);
            });
        Assert.All(telemetry.Events, telemetryEvent =>
        {
            Assert.Equal(64, telemetryEvent.Tags["tenant_id_hash"].Length);
            Assert.Equal(64, telemetryEvent.Tags["subject_id_hash"].Length);
        });
        Assert.All(telemetry.Events, AssertNoSensitiveTelemetry);
    }

    [Fact]
    public async Task Submit_records_audit_event_separately_from_telemetry()
    {
        var telemetry = new InMemoryProcessSummaryTelemetry();
        var audit = new InMemoryProcessSummaryAuditSink();
        var service = CreateService(telemetry, audit: audit);

        var job = await service.SubmitAsync(
            "idem-audit-1",
            AuthorizedCaller(),
            Source(File.ReadAllText(FixturePath)),
            "Resuma o processo.",
            CancellationToken.None);

        var auditEvent = Assert.Single(audit.Events);
        Assert.Equal("process_summary.submit", auditEvent.Action);
        Assert.Equal("validated", auditEvent.Outcome);
        Assert.Equal(job.JobId, auditEvent.JobId);
        Assert.Equal(job.CaseId, auditEvent.CaseId);
        Assert.Equal(64, auditEvent.SubjectIdHash.Length);
        Assert.DoesNotContain("cpf", auditEvent.ReasonCode, StringComparison.OrdinalIgnoreCase);
        Assert.NotEmpty(telemetry.Events);
    }

    [Fact]
    public async Task Submit_records_structured_log_without_raw_process_data()
    {
        var logger = new InMemoryProcessSummaryStructuredLogger();
        var service = CreateService(structuredLogger: logger);

        var job = await service.SubmitAsync(
            "idem-log-1",
            AuthorizedCaller(),
            Source(File.ReadAllText(FixturePath)),
            "Resuma o processo.",
            CancellationToken.None);

        var logEvent = Assert.Single(logger.Events);
        Assert.Equal("process_summary.submit", logEvent.EventName);
        Assert.Equal("validated", logEvent.Outcome);
        Assert.Equal(job.JobId, logEvent.JobId);
        Assert.Equal(job.CaseId, logEvent.CaseId);
        Assert.Equal(64, logEvent.TenantIdHash.Length);
        Assert.Equal(64, logEvent.SubjectIdHash.Length);
        Assert.DoesNotContain("02727135971", string.Join('|', logger.Events.SelectMany(item => new[]
        {
            item.EventName,
            item.Outcome,
            item.JobId ?? string.Empty,
            item.CaseId ?? string.Empty,
            item.TenantIdHash,
            item.SubjectIdHash
        })));
    }

    [Fact]
    public async Task Job_store_rejects_non_terminal_publication()
    {
        var service = CreateService();
        var validJob = await service.SubmitAsync(
            "idem-store-invariant",
            AuthorizedCaller(),
            Source(File.ReadAllText(FixturePath)),
            "Resuma o processo.",
            CancellationToken.None);
        var store = new InMemoryProcessSummaryJobStore();

        Assert.Throws<ArgumentException>(() => store.TryAdd(
            "tenant-1|subject-1|idem-store-invariant",
            validJob with { Status = ProcessSummaryJobStatus.Submitted }));
    }

    [Fact]
    public async Task Cancelled_submission_is_not_published_to_the_job_store()
    {
        var store = new InMemoryProcessSummaryJobStore();
        var service = CreateService(jobStore: store);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() => service.SubmitAsync(
            "idem-cancelled",
            AuthorizedCaller(),
            Source(File.ReadAllText(FixturePath)),
            "Resuma o processo.",
            cancellation.Token));

        Assert.Empty(await store.RecoverIncompleteAsync(CancellationToken.None));
        Assert.Null(store.GetByIdempotencyKey("tenant-1|subject-1|idem-cancelled"));
    }

    [Fact]
    public async Task Cancellation_during_generation_is_not_published_to_the_job_store()
    {
        var store = new InMemoryProcessSummaryJobStore();
        var model = new CancellationAwareModel();
        var service = CreateService(model: model, jobStore: store);
        using var cancellation = new CancellationTokenSource();
        var submission = service.SubmitAsync(
            "idem-cancelled-during-generation",
            AuthorizedCaller(),
            Source(File.ReadAllText(FixturePath)),
            "Resuma o processo.",
            cancellation.Token);

        await model.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => submission);
        Assert.Empty(await store.RecoverIncompleteAsync(CancellationToken.None));
        Assert.Null(store.GetByIdempotencyKey("tenant-1|subject-1|idem-cancelled-during-generation"));
    }

    [Fact]
    public async Task Audit_failure_is_not_reported_as_telemetry_failure_and_job_is_not_published()
    {
        var telemetry = new InMemoryProcessSummaryTelemetry();
        var service = CreateService(
            telemetry,
            audit: new ThrowingAuditSink());

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => service.SubmitAsync(
            "idem-audit-failure",
            AuthorizedCaller(),
            Source(File.ReadAllText(FixturePath)),
            "Resuma o processo.",
            CancellationToken.None));

        Assert.Equal("Audit sink failure.", exception.Message);
        Assert.Contains(telemetry.Events, item => item.Status == ProcessSummaryJobTelemetryStatus.Validated);
        Assert.DoesNotContain(telemetry.Events, item => item.Status == ProcessSummaryJobTelemetryStatus.Failed);
    }

    [Fact]
    public async Task Submit_records_end_to_end_duration_from_submission_to_terminal_status()
    {
        var telemetry = new InMemoryProcessSummaryTelemetry();
        var service = CreateService(
            telemetry,
            clock: new SequenceClock(
                SubmittedAt,
                SubmittedAt.AddSeconds(3),
                SubmittedAt.AddSeconds(3),
                SubmittedAt.AddSeconds(3)));
        var source = Source(File.ReadAllText(FixturePath));

        var job = await service.SubmitAsync("idem-job-duration", AuthorizedCaller(), source, "Resuma o processo.", CancellationToken.None);

        Assert.Equal(SubmittedAt, job.CreatedAt);
        Assert.Equal(SubmittedAt.AddSeconds(3), job.ValidatedAt);
        var validated = Assert.Single(telemetry.Events, item => item.Status == ProcessSummaryJobTelemetryStatus.Validated);
        Assert.Equal("3000", validated.Tags["duration_ms"]);
        AssertNoSensitiveTelemetry(validated);
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
        var replay = Assert.Single(telemetry.Events, item => item.Status == ProcessSummaryJobTelemetryStatus.IdempotentReplay);
        Assert.Equal("process_summary.idempotent_replay", replay.EventName);
        Assert.Equal(64, replay.Tags["tenant_id_hash"].Length);
        Assert.Equal(64, replay.Tags["subject_id_hash"].Length);
        AssertNoSensitiveTelemetry(replay);
    }

    [Fact]
    public async Task Submit_authorizes_caller_before_idempotent_replay()
    {
        var telemetry = new InMemoryProcessSummaryTelemetry();
        var service = CreateService(telemetry);
        var source = Source(File.ReadAllText(FixturePath));
        _ = await service.SubmitAsync("idem-job-auth-replay", AuthorizedCaller(), source, "Resuma o processo.", CancellationToken.None);

        var exception = await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.SubmitAsync(
                "idem-job-auth-replay",
                new CallerContext("tenant-1", "subject-1", ["other-case"], false),
                source,
                "Resuma o processo.",
                CancellationToken.None));

        Assert.Equal("Caller is not authorized for this legal case.", exception.Message);
        Assert.Contains(telemetry.Events, item => item.Status == ProcessSummaryJobTelemetryStatus.Forbidden);
        Assert.DoesNotContain(telemetry.Events, item => item.Status == ProcessSummaryJobTelemetryStatus.IdempotentReplay);
        Assert.All(telemetry.Events, AssertNoSensitiveTelemetry);
    }

    [Fact]
    public async Task Submit_scopes_idempotency_replay_to_caller_identity()
    {
        var telemetry = new InMemoryProcessSummaryTelemetry();
        var service = CreateService(telemetry);
        var source = Source(File.ReadAllText(FixturePath));
        var firstCaller = new CallerContext("tenant-1", "subject-1", ["response_60031603620268160021_1"], false);
        var secondCaller = new CallerContext("tenant-2", "subject-2", ["response_60031603620268160021_1"], false);

        var first = await service.SubmitAsync("idem-job-scoped", firstCaller, source, "Resuma o processo.", CancellationToken.None);
        var second = await service.SubmitAsync("idem-job-scoped", secondCaller, source, "Resuma o processo.", CancellationToken.None);
        var replay = await service.SubmitAsync("idem-job-scoped", firstCaller, source, "Resuma o processo novamente.", CancellationToken.None);

        Assert.NotEqual(first.JobId, second.JobId);
        Assert.Same(first, replay);
        Assert.Same(first, service.GetJob(first.JobId));
        Assert.Same(second, service.GetJob(second.JobId));
        Assert.Single(telemetry.Events, item => item.Status == ProcessSummaryJobTelemetryStatus.IdempotentReplay);
        Assert.All(telemetry.Events, AssertNoSensitiveTelemetry);
    }

    [Fact]
    public async Task Submit_accepts_distinct_idempotency_keys_for_same_snapshot_without_job_id_collision()
    {
        var service = CreateService();
        var source = Source(File.ReadAllText(FixturePath));

        var first = await service.SubmitAsync("idem-job-distinct-1", AuthorizedCaller(), source, "Resuma o processo.", CancellationToken.None);
        var second = await service.SubmitAsync("idem-job-distinct-2", AuthorizedCaller(), source, "Resuma o processo.", CancellationToken.None);

        Assert.NotEqual(first.JobId, second.JobId);
        Assert.Same(first, service.GetJob(first.JobId));
        Assert.Same(second, service.GetJob(second.JobId));
        Assert.Same(first.Output, service.GetValidatedSummary(first.JobId));
        Assert.Same(second.Output, service.GetValidatedSummary(second.JobId));
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
        var conflict = Assert.Single(telemetry.Events, item => item.Status == ProcessSummaryJobTelemetryStatus.IdempotencyConflict);
        Assert.Equal("process_summary.idempotency_conflict", conflict.EventName);
        Assert.Equal(64, conflict.Tags["tenant_id_hash"].Length);
        Assert.Equal(64, conflict.Tags["subject_id_hash"].Length);
        AssertNoSensitiveTelemetry(conflict);
    }

    [Fact]
    public async Task Submit_rechecks_snapshot_conflict_after_concurrent_generation_race()
    {
        var model = new BarrierProcessSummaryModel(2);
        var service = CreateService(model: model);
        var raw = File.ReadAllText(FixturePath);
        var changedSnapshot = raw.Replace("\"cached\":false", "\"cached\":true", StringComparison.Ordinal);

        var first = service.SubmitAsync("idem-job-race", AuthorizedCaller(), Source(raw), "Resuma o processo.", CancellationToken.None);
        var second = service.SubmitAsync("idem-job-race", AuthorizedCaller(), Source(changedSnapshot), "Resuma o processo.", CancellationToken.None);
        var results = await Task.WhenAll(Capture(first), Capture(second));

        Assert.Single(results, item => item.Job is not null);
        var exception = Assert.Single(results, item => item.Exception is not null).Exception;
        Assert.IsType<InvalidOperationException>(exception);
        Assert.Equal("Idempotency key is already bound to a different process snapshot.", exception.Message);
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
        var forbidden = Assert.Single(telemetry.Events, item => item.Status == ProcessSummaryJobTelemetryStatus.Forbidden);
        Assert.Equal("process_summary.forbidden", forbidden.EventName);
        Assert.Equal(64, forbidden.Tags["tenant_id_hash"].Length);
        Assert.Equal(64, forbidden.Tags["subject_id_hash"].Length);
        AssertNoSensitiveTelemetry(forbidden);
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
        var telemetry = new InMemoryProcessSummaryTelemetry();
        var service = CreateService(telemetry);
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
        Assert.Contains(telemetry.Events, telemetryEvent =>
            telemetryEvent.Status == ProcessSummaryJobTelemetryStatus.Freshness
            && telemetryEvent.EventName == "process_summary.freshness"
            && telemetryEvent.Tags["decision"] == "Stale");
        Assert.Contains(telemetry.Events, telemetryEvent =>
            telemetryEvent.Status == ProcessSummaryJobTelemetryStatus.RefreshPlan
            && telemetryEvent.EventName == "process_summary.refresh_plan"
            && telemetryEvent.Tags["decision"] == "Stale"
            && telemetryEvent.Tags["action"] == "Refresh"
            && telemetryEvent.Tags["reason"] == "freshness_policy"
            && telemetryEvent.Tags["requires_scheduler"] == "true");
        Assert.All(telemetry.Events, AssertNoSensitiveTelemetry);
    }

    [Fact]
    public void Refresh_plan_validates_current_state_before_missing_job_lookup()
    {
        var service = CreateService();

        var exception = Assert.Throws<ArgumentException>(() =>
            service.GetRefreshPlan("missing", " ", "version with cpf 02727135971"));

        Assert.Equal("currentSnapshotSha256", exception.ParamName);
        Assert.DoesNotContain("02727135971", exception.Message, StringComparison.Ordinal);

        var versionException = Assert.Throws<ArgumentException>(() =>
            service.GetRefreshPlan("missing", new string('a', 64), " "));

        Assert.Equal("currentSummaryVersion", versionException.ParamName);

        var hashFormatException = Assert.Throws<ArgumentException>(() =>
            service.GetRefreshPlan("missing", "not-a-sha256-with-cpf-02727135971", ProcessSummaryPrompt.PromptVersion));

        Assert.Equal("currentSnapshotSha256", hashFormatException.ParamName);
        Assert.DoesNotContain("02727135971", hashFormatException.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Refresh_plan_normalizes_uppercase_current_snapshot_hash()
    {
        var service = CreateService();
        var source = Source(File.ReadAllText(FixturePath));
        var job = await service.SubmitAsync("idem-job-10", AuthorizedCaller(), source, "Resuma o processo.", CancellationToken.None);

        var plan = service.GetRefreshPlan(job.JobId, job.SnapshotSha256.ToUpperInvariant(), job.SummaryVersion);

        Assert.NotNull(plan);
        Assert.Equal(ProcessSummaryRefreshAction.None, plan.Action);
        Assert.Equal("Summary is fresh.", plan.Reason);
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

    [Fact]
    public async Task Submit_records_sanitized_validation_failure_telemetry()
    {
        var telemetry = new InMemoryProcessSummaryTelemetry();
        var service = CreateService(telemetry, model: new IncompleteProcessSummaryModel());
        var source = Source(File.ReadAllText(FixturePath));

        var job = await service.SubmitAsync("idem-job-9", AuthorizedCaller(), source, "Resuma o processo.", CancellationToken.None);

        Assert.Equal(ProcessSummaryJobStatus.Failed, job.Status);
        Assert.False(job.Validation.IsValid);
        var failed = Assert.Single(telemetry.Events, item => item.Status == ProcessSummaryJobTelemetryStatus.Failed);
        Assert.Equal("process_summary.failed", failed.EventName);
        Assert.Equal("invalid", failed.Tags["validator_status"]);
        Assert.Equal("validator_failed", failed.Tags["validation_error_reason"]);
        Assert.Equal(job.Validation.Errors.Count.ToString(CultureInfo.InvariantCulture), failed.Tags["validation_error_count"]);
        Assert.DoesNotContain(job.Validation.Errors, error => failed.Tags.Values.Contains(error, StringComparer.Ordinal));
        AssertNoSensitiveTelemetry(failed);
    }

    [Fact]
    public async Task Emitted_telemetry_events_are_declared_in_observability_catalog()
    {
        var telemetry = new InMemoryProcessSummaryTelemetry();
        var service = CreateService(telemetry);
        var raw = File.ReadAllText(FixturePath);
        var source = Source(raw);
        var job = await service.SubmitAsync("idem-job-catalog-1", AuthorizedCaller(), source, "Resuma o processo.", CancellationToken.None);
        _ = service.GetRefreshPlan(job.JobId, new string('b', 64), job.SummaryVersion);
        _ = await service.SubmitAsync("idem-job-catalog-1", AuthorizedCaller(), source, "Resuma o processo.", CancellationToken.None);

        var changedSource = Source(raw.Replace("\"cached\":false", "\"cached\":true", StringComparison.Ordinal));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.SubmitAsync("idem-job-catalog-1", AuthorizedCaller(), changedSource, "Resuma o processo.", CancellationToken.None));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.SubmitAsync(
                "idem-job-catalog-2",
                new CallerContext("tenant-1", "subject-1", ["other-case"], false),
                source,
                "Resuma o processo.",
                CancellationToken.None));

        var failedService = CreateService(telemetry, model: new IncompleteProcessSummaryModel());
        _ = await failedService.SubmitAsync("idem-job-catalog-3", AuthorizedCaller(), source, "Resuma o processo.", CancellationToken.None);

        var traces = ProcessSummaryObservabilityCatalog.Current().Traces.ToDictionary(item => item.Name, StringComparer.Ordinal);
        foreach (var telemetryEvent in telemetry.Events)
        {
            Assert.True(traces.TryGetValue(telemetryEvent.EventName, out var trace), telemetryEvent.EventName);
            foreach (var attribute in ObservedAttributes(telemetryEvent))
            {
                Assert.Contains(attribute, trace.Attributes);
            }
        }
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

    private static ProcessSummaryJobService CreateService(
        InMemoryProcessSummaryTelemetry? telemetry = null,
        IProcessSummaryAuditSink? audit = null,
        IGenerationModel? model = null,
        IProcessSummaryClock? clock = null,
        IProcessSummaryStructuredLogger? structuredLogger = null,
        IProcessSummaryJobStore? jobStore = null)
    {
        var canonicalization = new ProcessSourceCanonicalizationService(new[] { new JuditProcessSourceAdapter() });
        var composer = new ProcessGenerationContextComposer(new GenerationContextBuilder());
        var generation = new GenerationService(model ?? new DeterministicProcessSummaryModel());
        return new ProcessSummaryJobService(
            canonicalization,
            composer,
            generation,
            clock ?? new FixedClock(SubmittedAt),
            telemetry ?? new InMemoryProcessSummaryTelemetry(),
            new EmptyProcessAttachmentContentStore(),
            audit,
            jobStore: jobStore,
            structuredLogger: structuredLogger);
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
            Assert.DoesNotContain("tenant-1", value, StringComparison.Ordinal);
            Assert.DoesNotContain("subject-1", value, StringComparison.Ordinal);
            Assert.DoesNotContain("tenant-2", value, StringComparison.Ordinal);
            Assert.DoesNotContain("subject-2", value, StringComparison.Ordinal);
            foreach (var token in RawSensitiveTokens)
            {
                Assert.DoesNotContain(token, value, StringComparison.Ordinal);
            }
        }
    }

    private static string[] ObservedAttributes(ProcessSummaryTelemetryEvent telemetryEvent)
    {
        var attributes = new List<string>();
        if (telemetryEvent.JobId is not null)
        {
            attributes.Add("job_id");
        }

        if (telemetryEvent.CaseId is not null)
        {
            attributes.Add("case_id");
        }

        if (telemetryEvent.Cnj is not null)
        {
            attributes.Add("cnj");
        }

        if (telemetryEvent.SnapshotSha256 is not null)
        {
            attributes.Add("snapshot_sha256");
        }

        if (telemetryEvent.SummaryVersion is not null)
        {
            attributes.Add("summary_version");
        }

        attributes.AddRange(telemetryEvent.Tags.Keys.Where(key => key != "component"));
        return attributes.Distinct(StringComparer.Ordinal).ToArray();
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : IProcessSummaryClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }

    private sealed class SequenceClock(params DateTimeOffset[] values) : IProcessSummaryClock
    {
        private int index;

        public DateTimeOffset UtcNow
        {
            get
            {
                var value = values[Math.Min(index, values.Length - 1)];
                index++;
                return value;
            }
        }
    }

    private sealed class IncompleteProcessSummaryModel : IGenerationModel
    {
        public Task<GenerationModelOutput> GenerateAsync(GenerationContext context, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var cnj = context.Items.First(item => item.Excerpt.StartsWith("CNJ:", StringComparison.Ordinal));
            return Task.FromResult(new GenerationModelOutput(
                false,
                null,
                [
                    new GenerationClaim(
                        cnj.Excerpt,
                        [new GenerationCitation(cnj.DocumentId, cnj.ContentSha256, cnj.Position.StartOffset, cnj.Position.Length)])
                ]));
        }
    }

    private sealed class ThrowingAuditSink : IProcessSummaryAuditSink
    {
        public void Record(ProcessSummaryAuditEvent auditEvent) =>
            throw new InvalidOperationException("Audit sink failure.");
    }

    private sealed class CancellationAwareModel : IGenerationModel
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<GenerationModelOutput> GenerateAsync(
            GenerationContext context,
            CancellationToken cancellationToken)
        {
            Started.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("Generation should have been cancelled.");
        }
    }

    private sealed class BarrierProcessSummaryModel(int expectedCalls) : IGenerationModel
    {
        private readonly DeterministicProcessSummaryModel inner = new();
        private readonly TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int calls;

        public async Task<GenerationModelOutput> GenerateAsync(GenerationContext context, CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref calls) == expectedCalls)
            {
                release.SetResult();
            }

            await release.Task.WaitAsync(cancellationToken);
            return await inner.GenerateAsync(context, cancellationToken);
        }
    }

    private static async Task<(ProcessSummaryJob? Job, Exception? Exception)> Capture(Task<ProcessSummaryJob> task)
    {
        try
        {
            return (await task, null);
        }
        catch (Exception exception)
        {
            return (null, exception);
        }
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
