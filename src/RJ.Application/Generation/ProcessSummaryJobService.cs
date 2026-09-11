using System.Security.Cryptography;
using System.Text;
using RJ.Application.Sources;
using RJ.Application.Security;
using RJ.Application.Operations;

namespace RJ.Application.Generation;

public sealed class ProcessSummaryJobService(
    ProcessSourceCanonicalizationService canonicalization,
    ProcessGenerationContextComposer composer,
    GenerationService generation,
    IProcessSummaryClock clock,
    IProcessSummaryTelemetry telemetry,
    IProcessAttachmentContentStore attachmentContentStore,
    IProcessSummaryAuditSink? auditSink = null,
    IProcessSummaryJobStore? jobStore = null,
    IProcessSummaryStructuredLogger? structuredLogger = null)
{
    private readonly IProcessSummaryAuditSink audit = auditSink ?? new NoopProcessSummaryAuditSink();
    private readonly IProcessSummaryJobStore store = jobStore ?? new InMemoryProcessSummaryJobStore();
    private readonly IProcessSummaryStructuredLogger logger = structuredLogger ?? new NoopProcessSummaryStructuredLogger();

    public Task<ProcessSummaryJob> SubmitAsync(
        string idempotencyKey,
        CallerContext caller,
        ProcessSourceDocument source,
        string instruction,
        CancellationToken cancellationToken) =>
        SubmitAsync(idempotencyKey, caller, source, instruction, [], cancellationToken);

    public async Task<ProcessSummaryJob> SubmitAsync(
        string idempotencyKey,
        CallerContext caller,
        ProcessSourceDocument source,
        string instruction,
        IReadOnlyList<ProcessAttachmentContent> requestAttachmentContents,
        CancellationToken cancellationToken)
    {
        var key = Require(idempotencyKey, nameof(idempotencyKey));
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(requestAttachmentContents);
        var callerTelemetryTags = CallerTelemetryTags(caller);
        var scopedKey = ScopedIdempotencyKey(caller, key);
        var legalCase = canonicalization.Canonicalize(source);
        var security = ProcessSecurityPolicy.AuthorizeProcessSummary(caller, legalCase);
        if (!security.IsAllowed)
        {
            Log("process_summary.submit", "forbidden", null, legalCase.Id.Value, callerTelemetryTags, clock.UtcNow);
            audit.Record(new ProcessSummaryAuditEvent(
                "process_summary.submit",
                "forbidden",
                null,
                legalCase.Id.Value,
                callerTelemetryTags["subject_id_hash"],
                clock.UtcNow,
                "caller_not_authorized"));
            telemetry.Record(Telemetry(
                ProcessSummaryJobTelemetryStatus.Forbidden,
                "process_summary.forbidden",
                null,
                legalCase.Id.Value,
                legalCase.Cnj.Value,
                source.RawContentSha256(),
                ProcessSummaryPrompt.PromptVersion,
                callerTelemetryTags));
            throw new UnauthorizedAccessException(security.DenialReason);
        }

        var existing = store.GetByIdempotencyKey(scopedKey);
        if (existing is not null)
        {
            if (!StringComparer.Ordinal.Equals(existing.SnapshotSha256, source.RawContentSha256()))
            {
                Log("process_summary.idempotency_conflict", "conflict", null, legalCase.Id.Value, callerTelemetryTags, clock.UtcNow);
                telemetry.Record(Telemetry(
                    ProcessSummaryJobTelemetryStatus.IdempotencyConflict,
                    "process_summary.idempotency_conflict",
                    null,
                    null,
                    null,
                    source.RawContentSha256(),
                    null,
                    callerTelemetryTags));
                throw new InvalidOperationException("Idempotency key is already bound to a different process snapshot.");
            }

            Log("process_summary.idempotent_replay", "replayed", existing.JobId, existing.CaseId, callerTelemetryTags, clock.UtcNow);
            telemetry.Record(Telemetry(
                ProcessSummaryJobTelemetryStatus.IdempotentReplay,
                "process_summary.idempotent_replay",
                existing.JobId,
                existing.CaseId,
                existing.Cnj,
                existing.SnapshotSha256,
                existing.SummaryVersion,
                callerTelemetryTags));
            return existing;
        }

        var submittedAt = clock.UtcNow;
        telemetry.Record(Telemetry(
            ProcessSummaryJobTelemetryStatus.Submitted,
            "process_summary.submitted",
            null,
            legalCase.Id.Value,
            legalCase.Cnj.Value,
            source.RawContentSha256(),
            ProcessSummaryPrompt.PromptVersion,
            MergeTags(callerTelemetryTags, new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["retrieval_calls"] = "0"
            })));

        var attachmentContents = ProcessAttachmentContentAdmission.Admit(
            legalCase,
            attachmentContentStore.ListByCase(legalCase.Id.Value)
                .Concat(requestAttachmentContents)
                .ToArray());
        var context = composer.Compose(legalCase, attachmentContents, ProcessSummaryPrompt.BuildQuery(instruction), 12000);
        var retry = await ProcessSummaryCorrectiveRetry.GenerateAsync(
            generation,
            legalCase,
            context,
            null,
            cancellationToken);
        var output = retry.Output;
        var validation = retry.Validation;
        var now = clock.UtcNow;
        var job = new ProcessSummaryJob(
            $"process-summary-{legalCase.Id.Value}-{source.RawContentSha256()[..12]}-{Hash(scopedKey)[..12]}",
            key,
            legalCase.Id.Value,
            legalCase.Cnj.Value,
            source.RawContentSha256(),
            ProcessSummaryPrompt.PromptVersion,
            0,
            retry.Attempts,
            retry.Retried,
            submittedAt,
            now,
            validation.IsValid ? ProcessSummaryJobStatus.Validated : ProcessSummaryJobStatus.Failed,
            output,
            validation,
            [
                new ProcessSummaryJobEvent(ProcessSummaryJobStatus.Submitted, submittedAt, "Job accepted for deterministic local processing."),
                new ProcessSummaryJobEvent(
                    validation.IsValid ? ProcessSummaryJobStatus.Validated : ProcessSummaryJobStatus.Failed,
                    now,
                    validation.IsValid ? "Summary validated." : "Summary failed validation.")
            ]);
        var terminalTags = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["attempts"] = job.Attempts.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["duration_ms"] = (job.ValidatedAt - job.CreatedAt).TotalMilliseconds.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["retried"] = job.Retried.ToString(System.Globalization.CultureInfo.InvariantCulture).ToLowerInvariant(),
            ["validator_status"] = validation.IsValid ? "valid" : "invalid",
            ["tenant_id_hash"] = callerTelemetryTags["tenant_id_hash"],
            ["subject_id_hash"] = callerTelemetryTags["subject_id_hash"]
        };
        if (!validation.IsValid)
        {
            terminalTags["validation_error_count"] = validation.Errors.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
            terminalTags["validation_error_reason"] = "validator_failed";
        }

        telemetry.Record(Telemetry(
            validation.IsValid ? ProcessSummaryJobTelemetryStatus.Validated : ProcessSummaryJobTelemetryStatus.Failed,
            validation.IsValid ? "process_summary.validated" : "process_summary.failed",
            job.JobId,
            job.CaseId,
            job.Cnj,
            job.SnapshotSha256,
            job.SummaryVersion,
            terminalTags));

        if (!store.TryAdd(scopedKey, job))
        {
            var raced = store.GetByIdempotencyKey(scopedKey);
            if (raced is not null)
            {
                if (!StringComparer.Ordinal.Equals(raced.SnapshotSha256, source.RawContentSha256()))
                {
                    Log("process_summary.idempotency_conflict", "conflict", null, legalCase.Id.Value, callerTelemetryTags, clock.UtcNow);
                    telemetry.Record(Telemetry(
                        ProcessSummaryJobTelemetryStatus.IdempotencyConflict,
                        "process_summary.idempotency_conflict",
                        null,
                        null,
                        null,
                        source.RawContentSha256(),
                        null,
                        callerTelemetryTags));
                    throw new InvalidOperationException("Idempotency key is already bound to a different process snapshot.");
                }

                return raced;
            }

            throw new InvalidOperationException("Process summary job could not be stored.");
        }

        Log(
            "process_summary.submit",
            validation.IsValid ? "validated" : "failed",
            job.JobId,
            job.CaseId,
            callerTelemetryTags,
            now);
        audit.Record(new ProcessSummaryAuditEvent(
            "process_summary.submit",
            validation.IsValid ? "validated" : "failed",
            job.JobId,
            job.CaseId,
            callerTelemetryTags["subject_id_hash"],
            now,
            validation.IsValid ? "validation_passed" : "validation_failed"));

        return job;
    }

    private void Log(
        string eventName,
        string outcome,
        string? jobId,
        string? caseId,
        Dictionary<string, string> callerTags,
        DateTimeOffset observedAt) =>
        logger.Record(new ProcessSummaryLogEvent(
            eventName,
            outcome,
            jobId,
            caseId,
            callerTags["tenant_id_hash"],
            callerTags["subject_id_hash"],
            observedAt));

    private ProcessSummaryTelemetryEvent Telemetry(
        ProcessSummaryJobTelemetryStatus status,
        string eventName,
        string? jobId,
        string? caseId,
        string? cnj,
        string? snapshotSha256,
        string? summaryVersion = null,
        IReadOnlyDictionary<string, string>? tags = null) =>
        new(
            eventName,
            jobId,
            caseId,
            cnj,
            snapshotSha256,
            summaryVersion,
            status,
            clock.UtcNow,
            BuildTags(tags));

    private static Dictionary<string, string> BuildTags(IReadOnlyDictionary<string, string>? tags)
    {
        var merged = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["component"] = "process-summary-job"
            };

        if (tags is not null)
        {
            foreach (var tag in tags)
            {
                merged[tag.Key] = tag.Value;
            }
        }

        return merged;
    }

    private static Dictionary<string, string> MergeTags(
        IReadOnlyDictionary<string, string> first,
        IReadOnlyDictionary<string, string> second)
    {
        var merged = new Dictionary<string, string>(first, StringComparer.Ordinal);
        foreach (var tag in second)
        {
            merged[tag.Key] = tag.Value;
        }

        return merged;
    }

    private static Dictionary<string, string> CallerTelemetryTags(CallerContext caller) =>
        new(StringComparer.Ordinal)
        {
            ["tenant_id_hash"] = Hash(caller.TenantId),
            ["subject_id_hash"] = Hash(caller.SubjectId)
        };

    private static string Hash(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    private static string ScopedIdempotencyKey(CallerContext caller, string idempotencyKey) =>
        string.Join(
            ':',
            Hash(caller.TenantId),
            Hash(caller.SubjectId),
            Hash(idempotencyKey));

    public ProcessSummaryJob? GetJob(string jobId)
    {
        var id = Require(jobId, nameof(jobId));
        return store.GetById(id);
    }

    public GenerationModelOutput? GetValidatedSummary(string jobId)
    {
        var job = GetJob(jobId);
        return job is { Status: ProcessSummaryJobStatus.Validated, Validation.IsValid: true } ? job.Output : null;
    }

    public ProcessSummaryRefreshPlan? GetRefreshPlan(
        string jobId,
        string currentSnapshotSha256,
        string currentSummaryVersion)
    {
        var snapshotSha256 = RequireSha256(currentSnapshotSha256, nameof(currentSnapshotSha256));
        var summaryVersion = Require(currentSummaryVersion, nameof(currentSummaryVersion));
        var job = GetJob(jobId);
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
        telemetry.Record(Telemetry(
            ProcessSummaryJobTelemetryStatus.Freshness,
            "process_summary.freshness",
            job.JobId,
            job.CaseId,
            job.Cnj,
            job.SnapshotSha256,
            job.SummaryVersion,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["decision"] = freshness.Status.ToString()
            }));
        var plan = ProcessSummaryRefreshPlanner.Plan(freshness);
        telemetry.Record(Telemetry(
            ProcessSummaryJobTelemetryStatus.RefreshPlan,
            "process_summary.refresh_plan",
            job.JobId,
            job.CaseId,
            job.Cnj,
            job.SnapshotSha256,
            job.SummaryVersion,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["decision"] = freshness.Status.ToString(),
                ["action"] = plan.Action.ToString(),
                ["reason"] = "freshness_policy",
                ["requires_scheduler"] = plan.RequiresScheduler.ToString(System.Globalization.CultureInfo.InvariantCulture).ToLowerInvariant()
            }));
        return plan;
    }

    private static string Require(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Value cannot be empty.", parameterName);
        }

        return value.Trim();
    }

    private static string RequireSha256(string value, string parameterName)
    {
        var trimmed = Require(value, parameterName);
        if (trimmed.Length != 64 || trimmed.Any(character => !Uri.IsHexDigit(character)))
        {
            throw new ArgumentException("SHA-256 value must contain exactly 64 hexadecimal characters.", parameterName);
        }

        return trimmed.ToLowerInvariant();
    }
}

public sealed record ProcessSummaryJob(
    string JobId,
    string IdempotencyKey,
    string CaseId,
    string Cnj,
    string SnapshotSha256,
    string SummaryVersion,
    int RetrievalCalls,
    int Attempts,
    bool Retried,
    DateTimeOffset CreatedAt,
    DateTimeOffset ValidatedAt,
    ProcessSummaryJobStatus Status,
    GenerationModelOutput Output,
    ProcessSummaryValidationResult Validation,
    IReadOnlyList<ProcessSummaryJobEvent> History);

public sealed record ProcessSummaryJobEvent(
    ProcessSummaryJobStatus Status,
    DateTimeOffset ObservedAt,
    string Reason);

public enum ProcessSummaryJobStatus
{
    Submitted,
    Validated,
    Failed
}
