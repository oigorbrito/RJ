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
    IProcessAttachmentContentStore attachmentContentStore)
{
    private readonly Dictionary<string, ProcessSummaryJob> jobsByIdempotencyKey = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ProcessSummaryJob> jobsById = new(StringComparer.Ordinal);
    private readonly object sync = new();

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

        lock (sync)
        {
            if (jobsByIdempotencyKey.TryGetValue(key, out var existing))
            {
                if (!StringComparer.Ordinal.Equals(existing.SnapshotSha256, source.RawContentSha256()))
                {
                    telemetry.Record(Telemetry(
                        ProcessSummaryJobTelemetryStatus.IdempotencyConflict,
                        "process_summary.idempotency_conflict",
                        null,
                        null,
                        null,
                        source.RawContentSha256()));
                    throw new InvalidOperationException("Idempotency key is already bound to a different process snapshot.");
                }

                telemetry.Record(Telemetry(
                    ProcessSummaryJobTelemetryStatus.IdempotentReplay,
                    "process_summary.idempotent_replay",
                    existing.JobId,
                    existing.CaseId,
                    existing.Cnj,
                    existing.SnapshotSha256,
                    existing.SummaryVersion));
                return existing;
            }
        }

        var legalCase = canonicalization.Canonicalize(source);
        var submittedAt = clock.UtcNow;
        telemetry.Record(Telemetry(
            ProcessSummaryJobTelemetryStatus.Submitted,
            "process_summary.submitted",
            null,
            legalCase.Id.Value,
            legalCase.Cnj.Value,
            source.RawContentSha256(),
            ProcessSummaryPrompt.PromptVersion));
        var security = ProcessSecurityPolicy.AuthorizeProcessSummary(caller, legalCase);
        if (!security.IsAllowed)
        {
            telemetry.Record(Telemetry(
                ProcessSummaryJobTelemetryStatus.Forbidden,
                "process_summary.forbidden",
                null,
                legalCase.Id.Value,
                legalCase.Cnj.Value,
                source.RawContentSha256(),
                ProcessSummaryPrompt.PromptVersion));
            throw new UnauthorizedAccessException(security.DenialReason);
        }

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
            $"process-summary-{legalCase.Id.Value}-{source.RawContentSha256()[..12]}",
            key,
            legalCase.Id.Value,
            legalCase.Cnj.Value,
            source.RawContentSha256(),
            ProcessSummaryPrompt.PromptVersion,
            0,
            retry.Attempts,
            retry.Retried,
            now,
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
        telemetry.Record(Telemetry(
            validation.IsValid ? ProcessSummaryJobTelemetryStatus.Validated : ProcessSummaryJobTelemetryStatus.Failed,
            validation.IsValid ? "process_summary.validated" : "process_summary.failed",
            job.JobId,
            job.CaseId,
            job.Cnj,
            job.SnapshotSha256,
            job.SummaryVersion,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["attempts"] = job.Attempts.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["retried"] = job.Retried.ToString(System.Globalization.CultureInfo.InvariantCulture).ToLowerInvariant()
            }));

        lock (sync)
        {
            if (jobsByIdempotencyKey.TryGetValue(key, out var raced))
            {
                return raced;
            }

            jobsByIdempotencyKey.Add(key, job);
            jobsById.Add(job.JobId, job);
        }

        return job;
    }

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

    public ProcessSummaryJob? GetJob(string jobId)
    {
        var id = Require(jobId, nameof(jobId));
        lock (sync)
        {
            return jobsById.GetValueOrDefault(id);
        }
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
        var job = GetJob(jobId);
        if (job is null)
        {
            return null;
        }

        var freshness = ProcessSummaryFreshnessPolicy.Evaluate(
            job,
            currentSnapshotSha256,
            currentSummaryVersion,
            clock.UtcNow,
            ProcessSecurityPolicy.DefaultRetentionPolicy());
        return ProcessSummaryRefreshPlanner.Plan(freshness);
    }

    private static string Require(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Value cannot be empty.", parameterName);
        }

        return value.Trim();
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
