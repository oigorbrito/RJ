using System.Security.Cryptography;
using System.Text;
using RJ.Application.Operations;
using RJ.Application.Security;

namespace RJ.Api;

public sealed class ApiAuthorizationEndpointFilter(
    IProcessSummaryCallerContextResolver callerResolver,
    IProcessSummaryAuditSink auditSink,
    IProcessSummaryClock clock) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        if (!callerResolver.TryResolve(context.HttpContext.User, out var caller) || caller is null)
        {
            Record("api.request", "unauthenticated", null, null);
            return Results.Unauthorized();
        }

        var caseId = ResolveCaseId(context);
        if (caseId is not null
            && !caller.AuthorizedCaseIds.Contains(caseId, StringComparer.Ordinal))
        {
            Record("api.request", "not_found_or_denied", caseId, caller);
            return Results.NotFound();
        }

        var ingestion = context.Arguments.OfType<IngestLegalDocumentRequest>().FirstOrDefault();
        if (ingestion is not null && !caller.IsAuthorizedForEvidenceSource(ingestion.SourceName))
        {
            Record("api.ingestion", "not_found_or_denied", ingestion.CaseId, caller);
            return Results.NotFound();
        }

        Record("api.request", "allowed", caseId, caller);
        return await next(context);
    }

    private static string? ResolveCaseId(EndpointFilterInvocationContext context)
    {
        if (context.HttpContext.Request.RouteValues.TryGetValue("caseId", out var routeCaseId)
            && routeCaseId is not null
            && !string.IsNullOrWhiteSpace(routeCaseId.ToString()))
        {
            return routeCaseId.ToString()!.Trim();
        }

        var ingestion = context.Arguments.OfType<IngestLegalDocumentRequest>().FirstOrDefault();
        return string.IsNullOrWhiteSpace(ingestion?.CaseId) ? null : ingestion.CaseId.Trim();
    }

    private void Record(
        string eventName,
        string decision,
        string? caseId,
        CallerContext? caller)
    {
        auditSink.Record(new ProcessSummaryAuditEvent(
            eventName,
            decision,
            null,
            caseId,
            caller is null ? null : Hash(caller.TenantId),
            caller is null ? null : Hash(caller.SubjectId),
            clock.UtcNow));
    }

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
