using System.Security.Cryptography;
using System.Text;
using RJ.Application.Generation;
using RJ.Application.Operations;
using RJ.Application.Security;

namespace RJ.Api;

public static class PersistentProcessSummaryEndpoint
{
    public static async Task<IResult> SubmitAsync(
        HttpContext httpContext,
        ProcessSummaryHttpRequest request,
        IProcessSummaryCallerContextResolver callerResolver,
        IProcessSummaryJobAccessStore accessStore,
        IProcessSummaryAuditSink auditSink,
        IProcessSummaryClock clock,
        ProcessSummaryPersistenceCoordinator persistence,
        ProcessSummaryJobService service,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(callerResolver);
        ArgumentNullException.ThrowIfNull(accessStore);
        ArgumentNullException.ThrowIfNull(auditSink);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(persistence);
        ArgumentNullException.ThrowIfNull(service);

        if (!callerResolver.TryResolve(httpContext.User, out var caller) || caller is null)
        {
            RecordAudit(auditSink, clock, "process_summary.submit", "unauthenticated", null, null, null);
            return Results.Unauthorized();
        }

        ProcessSummaryRequest internalRequest;
        string snapshotSha256;
        try
        {
            ArgumentNullException.ThrowIfNull(request);
            internalRequest = ToInternalRequest(request, caller);
            snapshotSha256 = HashRawContent(request.RawContent);
        }
        catch (ArgumentException)
        {
            RecordAudit(auditSink, clock, "process_summary.submit", "invalid_request", null, null, caller);
            return Results.BadRequest(new ProcessSummaryError("Invalid process summary request."));
        }

        try
        {
            var existing = await persistence.ResolveExistingSubmissionAsync(
                caller,
                request.IdempotencyKey,
                snapshotSha256,
                cancellationToken);
            if (existing is not null)
            {
                if (!await accessStore.CanAccessAsync(existing.JobId, caller, cancellationToken))
                {
                    RecordAudit(auditSink, clock, "process_summary.submit", "not_found_or_denied", existing.JobId, existing.CaseId, caller);
                    return Results.NotFound();
                }

                RecordAudit(auditSink, clock, "process_summary.submit", "persisted_replay", existing.JobId, existing.CaseId, caller);
                return Results.Accepted($"/api/process-summaries/jobs/{existing.JobId}", ToResponse(existing));
            }

            var result = await ProcessSummaryEndpoint.SubmitAsync(internalRequest, service, cancellationToken);
            if (result is not IValueHttpResult { Value: ProcessSummaryJobResponse response }
                || result is not IStatusCodeHttpResult { StatusCode: StatusCodes.Status202Accepted })
            {
                return result;
            }

            var generated = service.GetJob(response.JobId);
            if (generated is null)
            {
                RecordAudit(auditSink, clock, "process_summary.submit", "persistence_source_missing", response.JobId, response.CaseId, caller);
                return Results.Conflict(new ProcessSummaryError("Process summary request could not be completed."));
            }

            var persisted = await persistence.PersistSubmissionAsync(
                caller,
                request.IdempotencyKey,
                generated,
                cancellationToken);

            if (!await accessStore.TryBindAsync(persisted.JobId, caller, persisted.CaseId, cancellationToken))
            {
                RecordAudit(auditSink, clock, "process_summary.submit", "ownership_conflict", persisted.JobId, persisted.CaseId, caller);
                return Results.Conflict(new ProcessSummaryError("Process summary request could not be completed."));
            }

            RecordAudit(auditSink, clock, "process_summary.submit", "allowed", persisted.JobId, persisted.CaseId, caller);
            return Results.Accepted($"/api/process-summaries/jobs/{persisted.JobId}", ToResponse(persisted));
        }
        catch (InvalidOperationException exception)
        {
            RecordAudit(auditSink, clock, "process_summary.submit", "idempotency_conflict", null, null, caller);
            return Results.Conflict(new ProcessSummaryError(ToPublicConflictError(exception)));
        }
    }

    public static async Task<IResult> GetJobAsync(
        HttpContext httpContext,
        string jobId,
        IProcessSummaryCallerContextResolver callerResolver,
        IProcessSummaryJobAccessStore accessStore,
        IProcessSummaryAuditSink auditSink,
        IProcessSummaryClock clock,
        ProcessSummaryPersistenceCoordinator persistence,
        CancellationToken cancellationToken)
    {
        var authorization = await AuthorizeAsync(
            httpContext,
            jobId,
            "process_summary.job.read",
            callerResolver,
            accessStore,
            auditSink,
            clock,
            cancellationToken);
        if (authorization.Result is not null)
        {
            return authorization.Result;
        }

        try
        {
            var job = await persistence.GetJobAsync(jobId, cancellationToken);
            return job is null ? Results.NotFound() : Results.Ok(ToResponse(job));
        }
        catch (ArgumentException)
        {
            return Results.BadRequest(new ProcessSummaryError("Invalid process summary job request."));
        }
    }

    public static async Task<IResult> GetValidatedSummaryAsync(
        HttpContext httpContext,
        string jobId,
        IProcessSummaryCallerContextResolver callerResolver,
        IProcessSummaryJobAccessStore accessStore,
        IProcessSummaryAuditSink auditSink,
        IProcessSummaryClock clock,
        ProcessSummaryPersistenceCoordinator persistence,
        CancellationToken cancellationToken)
    {
        var authorization = await AuthorizeAsync(
            httpContext,
            jobId,
            "process_summary.validated_summary.read",
            callerResolver,
            accessStore,
            auditSink,
            clock,
            cancellationToken);
        if (authorization.Result is not null)
        {
            return authorization.Result;
        }

        try
        {
            var output = await persistence.GetValidatedSummaryAsync(jobId, cancellationToken);
            return output is null ? Results.NotFound() : Results.Ok(output);
        }
        catch (ArgumentException)
        {
            return Results.BadRequest(new ProcessSummaryError("Invalid process summary job request."));
        }
    }

    public static async Task<IResult> GetRefreshPlanAsync(
        HttpContext httpContext,
        string jobId,
        ProcessSummaryRefreshPlanRequest request,
        IProcessSummaryCallerContextResolver callerResolver,
        IProcessSummaryJobAccessStore accessStore,
        IProcessSummaryAuditSink auditSink,
        IProcessSummaryClock clock,
        ProcessSummaryPersistenceCoordinator persistence,
        CancellationToken cancellationToken)
    {
        var authorization = await AuthorizeAsync(
            httpContext,
            jobId,
            "process_summary.refresh_plan.read",
            callerResolver,
            accessStore,
            auditSink,
            clock,
            cancellationToken);
        if (authorization.Result is not null)
        {
            return authorization.Result;
        }

        try
        {
            ArgumentNullException.ThrowIfNull(request);
            var plan = await persistence.GetRefreshPlanAsync(
                jobId,
                request.CurrentSnapshotSha256,
                request.CurrentSummaryVersion,
                cancellationToken);
            return plan is null
                ? Results.NotFound()
                : Results.Ok(new ProcessSummaryRefreshPlanResponse(
                    plan.Action.ToString(),
                    plan.Reason,
                    plan.RequiresScheduler));
        }
        catch (ArgumentException)
        {
            return Results.BadRequest(new ProcessSummaryError("Invalid process summary refresh plan request."));
        }
    }

    private static async Task<AuthorizationResult> AuthorizeAsync(
        HttpContext httpContext,
        string jobId,
        string auditEventName,
        IProcessSummaryCallerContextResolver callerResolver,
        IProcessSummaryJobAccessStore accessStore,
        IProcessSummaryAuditSink auditSink,
        IProcessSummaryClock clock,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(callerResolver);
        ArgumentNullException.ThrowIfNull(accessStore);
        ArgumentNullException.ThrowIfNull(auditSink);
        ArgumentNullException.ThrowIfNull(clock);

        if (!callerResolver.TryResolve(httpContext.User, out var caller) || caller is null)
        {
            RecordAudit(auditSink, clock, auditEventName, "unauthenticated", jobId, null, null);
            return new AuthorizationResult(null, Results.Unauthorized());
        }

        try
        {
            if (!await accessStore.CanAccessAsync(jobId, caller, cancellationToken))
            {
                RecordAudit(auditSink, clock, auditEventName, "not_found_or_denied", jobId, null, caller);
                return new AuthorizationResult(caller, Results.NotFound());
            }
        }
        catch (ArgumentException)
        {
            RecordAudit(auditSink, clock, auditEventName, "invalid_request", null, null, caller);
            return new AuthorizationResult(caller, Results.BadRequest(new ProcessSummaryError("Invalid process summary job request.")));
        }

        RecordAudit(auditSink, clock, auditEventName, "allowed", jobId, null, caller);
        return new AuthorizationResult(caller, null);
    }

    private static ProcessSummaryRequest ToInternalRequest(
        ProcessSummaryHttpRequest request,
        CallerContext caller) =>
        new(
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
        job.Validation.IsValid ? [] : ["process_summary_validation_failed"],
        job.History.Select(item => new ProcessSummaryJobEventResponse(
            item.Status.ToString(),
            item.ObservedAt,
            item.Reason)).ToArray());

    private static string HashRawContent(string rawContent)
    {
        ArgumentNullException.ThrowIfNull(rawContent);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawContent))).ToLowerInvariant();
    }

    private static string ToPublicConflictError(InvalidOperationException exception) =>
        exception.Message switch
        {
            "Idempotency key is already bound to a different process snapshot." => exception.Message,
            _ => "Process summary request could not be completed."
        };

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
            caller is null ? null : ProcessSummaryPersistenceIdentity.TenantIdHash(caller),
            caller is null ? null : ProcessSummaryPersistenceIdentity.SubjectIdHash(caller),
            clock.UtcNow));
    }

    private sealed record AuthorizationResult(CallerContext? Caller, IResult? Result);
}
