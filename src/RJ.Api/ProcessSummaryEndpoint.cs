using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using RJ.Application.Generation;
using RJ.Application.Operations;
using RJ.Application.Security;
using RJ.Application.Sources;

namespace RJ.Api;

public static class ProcessSummaryEndpoint
{
    public static async Task<IResult> SubmitAuthenticatedAsync(
        HttpContext httpContext,
        ProcessSummaryHttpRequest request,
        IProcessSummaryCallerContextResolver callerResolver,
        IProcessSummaryJobAccessStore accessStore,
        IProcessSummaryAuditSink auditSink,
        IProcessSummaryClock clock,
        ProcessSummaryJobService service,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(callerResolver);
        ArgumentNullException.ThrowIfNull(accessStore);
        ArgumentNullException.ThrowIfNull(auditSink);
        ArgumentNullException.ThrowIfNull(clock);

        if (!callerResolver.TryResolve(httpContext.User, out var caller) || caller is null)
        {
            RecordAudit(auditSink, clock, "process_summary.submit", "unauthenticated", null, null, null);
            return Results.Unauthorized();
        }

        var result = await SubmitAsync(ToInternalRequest(request, caller), service, cancellationToken);
        if (result is IValueHttpResult { Value: ProcessSummaryJobResponse response }
            && result is IStatusCodeHttpResult { StatusCode: StatusCodes.Status202Accepted })
        {
            if (!accessStore.TryBind(response.JobId, caller, response.CaseId))
            {
                RecordAudit(auditSink, clock, "process_summary.submit", "ownership_conflict", response.JobId, response.CaseId, caller);
                return Results.Conflict(new ProcessSummaryError("Process summary request could not be completed."));
            }

            RecordAudit(auditSink, clock, "process_summary.submit", "allowed", response.JobId, response.CaseId, caller);
        }

        return result;
    }

    public static IResult GetJobAuthenticated(
        HttpContext httpContext,
        string jobId,
        IProcessSummaryCallerContextResolver callerResolver,
        IProcessSummaryJobAccessStore accessStore,
        IProcessSummaryAuditSink auditSink,
        IProcessSummaryClock clock,
        ProcessSummaryJobService service)
    {
        if (!TryAuthorizeJobAccess(
            httpContext,
            jobId,
            "process_summary.job.read",
            callerResolver,
            accessStore,
            auditSink,
            clock,
            out var unauthorized))
        {
            return unauthorized!;
        }

        return GetJob(jobId, service);
    }

    public static IResult GetValidatedSummaryAuthenticated(
        HttpContext httpContext,
        string jobId,
        IProcessSummaryCallerContextResolver callerResolver,
        IProcessSummaryJobAccessStore accessStore,
        IProcessSummaryAuditSink auditSink,
        IProcessSummaryClock clock,
        ProcessSummaryJobService service)
    {
        if (!TryAuthorizeJobAccess(
            httpContext,
            jobId,
            "process_summary.validated_summary.read",
            callerResolver,
            accessStore,
            auditSink,
            clock,
            out var unauthorized))
        {
            return unauthorized!;
        }

        return GetValidatedSummary(jobId, service);
    }

    public static IResult GetRefreshPlanAuthenticated(
        HttpContext httpContext,
        string jobId,
        ProcessSummaryRefreshPlanRequest request,
        IProcessSummaryCallerContextResolver callerResolver,
        IProcessSummaryJobAccessStore accessStore,
        IProcessSummaryAuditSink auditSink,
        IProcessSummaryClock clock,
        ProcessSummaryJobService service)
    {
        if (!TryAuthorizeJobAccess(
            httpContext,
            jobId,
            "process_summary.refresh_plan.read",
            callerResolver,
            accessStore,
            auditSink,
            clock,
            out var unauthorized))
        {
            return unauthorized!;
        }

        return GetRefreshPlan(jobId, request, service);
    }

    public static async Task<IResult> SubmitAsync(
        ProcessSummaryRequest request,
        ProcessSummaryJobService service,
        CancellationToken cancellationToken)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(request);

            if (request.AttachmentContents.Any(item => item is null))
            {
                return Results.BadRequest(new ProcessSummaryError("Invalid process summary request."));
            }

            if (Encoding.UTF8.GetByteCount(request.RawContent) > IngestionLimits.MaxRawContentBytes)
            {
                return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
            }

            if (request.AttachmentContents.Count > IngestionLimits.MaxProcessAttachmentContentItems
                || request.AttachmentContents.Any(item =>
                    Encoding.UTF8.GetByteCount(item.ExtractedText) > IngestionLimits.MaxProcessAttachmentTextBytes))
            {
                return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
            }

            var source = new ProcessSourceDocument(
                request.SourceSystem,
                request.SourceName,
                request.SourceReference,
                request.RawContent,
                DateTimeOffset.Parse(request.ObservedAt, CultureInfo.InvariantCulture));
            var caller = new CallerContext(
                request.TenantId,
                request.SubjectId,
                request.AuthorizedCaseIds,
                request.CanAccessSealedCases,
                request.AuthorizedEvidenceSourceNames);
            var attachmentContents = request.AttachmentContents
                .Select(item => new ProcessAttachmentContent(
                    item.CaseId,
                    item.AttachmentId,
                    item.SourceName,
                    item.SourceReference,
                    item.ExtractedText,
                    DateTimeOffset.Parse(item.ObservedAt, CultureInfo.InvariantCulture)))
                .ToArray();
            var job = await service.SubmitAsync(
                request.IdempotencyKey,
                caller,
                source,
                request.Instruction,
                attachmentContents,
                cancellationToken);
            return Results.Accepted($"/api/process-summaries/jobs/{job.JobId}", ToResponse(job));
        }
        catch (ArgumentNullException)
        {
            return Results.BadRequest(new ProcessSummaryError("Invalid process summary request."));
        }
        catch (ArgumentException)
        {
            return Results.BadRequest(new ProcessSummaryError("Invalid process summary request."));
        }
        catch (FormatException)
        {
            return Results.BadRequest(new ProcessSummaryError("Invalid process summary request."));
        }
        catch (JsonException)
        {
            return Results.BadRequest(new ProcessSummaryError("Invalid process summary request."));
        }
        catch (KeyNotFoundException)
        {
            return Results.BadRequest(new ProcessSummaryError("Invalid process summary request."));
        }
        catch (InvalidOperationException exception)
        {
            return Results.Conflict(new ProcessSummaryError(ToPublicConflictError(exception)));
        }
        catch (UnauthorizedAccessException)
        {
            return Results.Forbid();
        }
    }

    public static IResult GetJob(string jobId, ProcessSummaryJobService service)
    {
        try
        {
            var job = service.GetJob(jobId);
            return job is null ? Results.NotFound() : Results.Ok(ToResponse(job));
        }
        catch (ArgumentException)
        {
            return Results.BadRequest(new ProcessSummaryError("Invalid process summary job request."));
        }
    }

    public static IResult GetValidatedSummary(string jobId, ProcessSummaryJobService service)
    {
        try
        {
            var output = service.GetValidatedSummary(jobId);
            return output is null ? Results.NotFound() : Results.Ok(output);
        }
        catch (ArgumentException)
        {
            return Results.BadRequest(new ProcessSummaryError("Invalid process summary job request."));
        }
    }

    public static IResult GetRefreshPlan(
        string jobId,
        ProcessSummaryRefreshPlanRequest request,
        ProcessSummaryJobService service)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(request);
            var plan = service.GetRefreshPlan(jobId, request.CurrentSnapshotSha256, request.CurrentSummaryVersion);
            return plan is null
                ? Results.NotFound()
                : Results.Ok(new ProcessSummaryRefreshPlanResponse(
                    plan.Action.ToString(),
                    plan.Reason,
                    plan.RequiresScheduler));
        }
        catch (ArgumentNullException)
        {
            return Results.BadRequest(new ProcessSummaryError("Invalid process summary refresh plan request."));
        }
        catch (ArgumentException)
        {
            return Results.BadRequest(new ProcessSummaryError("Invalid process summary refresh plan request."));
        }
    }

    private static bool TryAuthorizeJobAccess(
        HttpContext httpContext,
        string jobId,
        string auditEventName,
        IProcessSummaryCallerContextResolver callerResolver,
        IProcessSummaryJobAccessStore accessStore,
        IProcessSummaryAuditSink auditSink,
        IProcessSummaryClock clock,
        out IResult? unauthorized)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(callerResolver);
        ArgumentNullException.ThrowIfNull(accessStore);
        ArgumentNullException.ThrowIfNull(auditSink);
        ArgumentNullException.ThrowIfNull(clock);
        unauthorized = null;

        if (!callerResolver.TryResolve(httpContext.User, out var caller) || caller is null)
        {
            RecordAudit(auditSink, clock, auditEventName, "unauthenticated", jobId, null, null);
            unauthorized = Results.Unauthorized();
            return false;
        }

        try
        {
            if (!accessStore.CanAccess(jobId, caller))
            {
                RecordAudit(auditSink, clock, auditEventName, "not_found_or_denied", jobId, null, caller);
                unauthorized = Results.NotFound();
                return false;
            }
        }
        catch (ArgumentException)
        {
            RecordAudit(auditSink, clock, auditEventName, "invalid_request", null, null, caller);
            unauthorized = Results.BadRequest(new ProcessSummaryError("Invalid process summary job request."));
            return false;
        }

        RecordAudit(auditSink, clock, auditEventName, "allowed", jobId, null, caller);
        return true;
    }

    private static void RecordAudit(
        IProcessSummaryAuditSink auditSink,
        IProcessSummaryClock clock,
        string eventName,
        string decision,
        string? jobId,
        string? caseId,
        CallerContext? caller)
    {
        auditSink.Record(new ProcessSummaryAuditEvent(
            eventName,
            decision,
            jobId,
            caseId,
            caller is null ? null : Hash(caller.TenantId),
            caller is null ? null : Hash(caller.SubjectId),
            clock.UtcNow));
    }

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static ProcessSummaryRequest ToInternalRequest(
        ProcessSummaryHttpRequest request,
        CallerContext caller)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(caller);
        return new ProcessSummaryRequest(
            request.IdempotencyKey,
            request.SourceSystem,
            request.SourceName,
            request.SourceReference,
            request.RawContent,
            request.ObservedAt,
            request.Instruction,
            caller.TenantId,
            caller.SubjectId,
            caller.AuthorizedCaseIds,
            caller.CanAccessSealedCases,
            caller.AuthorizedEvidenceSourceNames,
            request.AttachmentContents);
    }

    private static ProcessSummaryJobResponse ToResponse(ProcessSummaryJob job) => new(
        job.JobId,
        job.CaseId,
        job.Cnj,
        job.SnapshotSha256,
        job.SummaryVersion,
        job.RetrievalCalls,
        job.Attempts,
        job.Retried,
        job.CreatedAt,
        job.ValidatedAt,
        job.Status.ToString(),
        job.Validation.IsValid,
        ToPublicValidationErrors(job),
        job.History.Select(item => new ProcessSummaryJobEventResponse(
            item.Status.ToString(),
            item.ObservedAt,
            item.Reason)).ToArray());

    private static IReadOnlyList<string> ToPublicValidationErrors(ProcessSummaryJob job) =>
        job.Validation.IsValid ? [] : ["process_summary_validation_failed"];

    private static string ToPublicConflictError(InvalidOperationException exception) =>
        exception.Message switch
        {
            "Idempotency key is already bound to a different process snapshot." => exception.Message,
            "Attachment content must belong to the canonical legal case." => exception.Message,
            "Attachment content must reference observed attachment metadata." => exception.Message,
            _ => "Process summary request could not be completed."
        };
}

public sealed record ProcessSummaryHttpRequest(
    string IdempotencyKey,
    string SourceSystem,
    string SourceName,
    string SourceReference,
    string RawContent,
    string ObservedAt,
    string Instruction,
    IReadOnlyList<ProcessAttachmentContentRequest>? AttachmentContents = null)
{
    public IReadOnlyList<ProcessAttachmentContentRequest> AttachmentContents { get; } = AttachmentContents ?? [];
}

public sealed record ProcessSummaryRequest(
    string IdempotencyKey,
    string SourceSystem,
    string SourceName,
    string SourceReference,
    string RawContent,
    string ObservedAt,
    string Instruction,
    string TenantId,
    string SubjectId,
    IReadOnlyList<string> AuthorizedCaseIds,
    bool CanAccessSealedCases,
    IReadOnlyList<string>? AuthorizedEvidenceSourceNames = null,
    IReadOnlyList<ProcessAttachmentContentRequest>? AttachmentContents = null)
{
    public IReadOnlyList<ProcessAttachmentContentRequest> AttachmentContents { get; } = AttachmentContents ?? [];
}

public sealed record ProcessAttachmentContentRequest(
    string CaseId,
    string AttachmentId,
    string SourceName,
    string SourceReference,
    string ExtractedText,
    string ObservedAt);

public sealed record ProcessSummaryJobResponse(
    string JobId,
    string CaseId,
    string Cnj,
    string SnapshotSha256,
    string SummaryVersion,
    int RetrievalCalls,
    int Attempts,
    bool Retried,
    DateTimeOffset CreatedAt,
    DateTimeOffset ValidatedAt,
    string Status,
    bool IsValid,
    IReadOnlyList<string> Errors,
    IReadOnlyList<ProcessSummaryJobEventResponse> History);

public sealed record ProcessSummaryJobEventResponse(
    string Status,
    DateTimeOffset ObservedAt,
    string Reason);

public sealed record ProcessSummaryRefreshPlanRequest(
    string CurrentSnapshotSha256,
    string CurrentSummaryVersion);

public sealed record ProcessSummaryRefreshPlanResponse(
    string Action,
    string Reason,
    bool RequiresScheduler);

public sealed record ProcessSummaryError(string Error);
