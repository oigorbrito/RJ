using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using RJ.Application.Generation;
using RJ.Application.Security;
using RJ.Application.Sources;
using RJ.Api.Security;

namespace RJ.Api;

public static class ProcessSummaryEndpoint
{
    public static Task<IResult> SubmitAuthenticatedAsync(
        ProcessSummaryRequest request,
        CallerContext caller,
        ProcessSummaryJobService service,
        CancellationToken cancellationToken) =>
        SubmitCoreAsync(request, caller, service, cancellationToken);

    public static async Task<IResult> SubmitAsync(
        ProcessSummaryRequest request,
        ProcessSummaryJobService service,
        CancellationToken cancellationToken)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(request);
            var caller = request.AuthenticatedCallerContext
                ?? throw new UnauthorizedAccessException("An authenticated caller is required.");
            return await SubmitCoreAsync(request, caller, service, cancellationToken);
        }
        catch (ArgumentNullException)
        {
            return Results.BadRequest(new ProcessSummaryError("Invalid process summary request."));
        }
        catch (UnauthorizedAccessException)
        {
            return Results.Unauthorized();
        }
    }

    private static async Task<IResult> SubmitCoreAsync(
        ProcessSummaryRequest request,
        CallerContext caller,
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

    public static IResult GetJobAuthenticated(string jobId, CallerContext caller, ProcessSummaryJobService service)
    {
        try
        {
            var job = service.GetJob(jobId);
            if (job is null)
            {
                return Results.NotFound();
            }

            EnsureCallerCanReadJob(caller, job);
            return Results.Ok(ToResponse(job));
        }
        catch (ArgumentException)
        {
            return Results.BadRequest(new ProcessSummaryError("Invalid process summary job request."));
        }
        catch (ForbiddenAccessException)
        {
            return Results.Forbid();
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

    public static IResult GetValidatedSummaryAuthenticated(string jobId, CallerContext caller, ProcessSummaryJobService service)
    {
        try
        {
            var job = service.GetJob(jobId);
            if (job is null)
            {
                return Results.NotFound();
            }

            EnsureCallerCanReadJob(caller, job);
            return job is { Status: ProcessSummaryJobStatus.Validated, Validation.IsValid: true }
                ? Results.Ok(job.Output)
                : Results.NotFound();
        }
        catch (ArgumentException)
        {
            return Results.BadRequest(new ProcessSummaryError("Invalid process summary job request."));
        }
        catch (ForbiddenAccessException)
        {
            return Results.Forbid();
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

    public static IResult GetRefreshPlanAuthenticated(
        string jobId,
        ProcessSummaryRefreshPlanRequest request,
        CallerContext caller,
        ProcessSummaryJobService service)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(request);
            var job = service.GetJob(jobId);
            if (job is null)
            {
                return Results.NotFound();
            }

            EnsureCallerCanReadJob(caller, job);
            var plan = service.GetRefreshPlan(jobId, request.CurrentSnapshotSha256, request.CurrentSummaryVersion);
            return plan is null
                ? Results.NotFound()
                : Results.Ok(new ProcessSummaryRefreshPlanResponse(
                    plan.Action.ToString(), plan.Reason, plan.RequiresScheduler));
        }
        catch (ArgumentNullException)
        {
            return Results.BadRequest(new ProcessSummaryError("Invalid process summary refresh plan request."));
        }
        catch (ArgumentException)
        {
            return Results.BadRequest(new ProcessSummaryError("Invalid process summary refresh plan request."));
        }
        catch (ForbiddenAccessException)
        {
            return Results.Forbid();
        }
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

    private static void EnsureCallerCanReadJob(CallerContext caller, ProcessSummaryJob job)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(job);
        if (!caller.IsAuthorizedForCase(job.CaseId))
        {
            throw new ForbiddenAccessException("Caller is not authorized for this legal case.");
        }
    }

    private static string ToPublicConflictError(InvalidOperationException exception) =>
        exception.Message switch
        {
            "Idempotency key is already bound to a different process snapshot." => exception.Message,
            "Attachment content must belong to the canonical legal case." => exception.Message,
            "Attachment content must reference observed attachment metadata." => exception.Message,
            _ => "Process summary request could not be completed."
        };
}

public sealed record ProcessSummaryRequest(
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

    [JsonIgnore]
    public CallerContext? AuthenticatedCallerContext { get; init; }
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
