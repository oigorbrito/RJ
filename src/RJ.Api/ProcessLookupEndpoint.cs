using System.Text.Json;
using RJ.Application.Security;
using RJ.Domain.Cases;
using RJ.Infrastructure.Persistence;

namespace RJ.Api;

public static class ProcessLookupEndpoint
{
    public static async Task<IResult> GetByCnjAsync(
        HttpContext httpContext,
        string cnj,
        IProcessSummaryCallerContextResolver callerResolver,
        PostgresProcessCatalog catalog,
        CancellationToken cancellationToken)
    {
        if (!callerResolver.TryResolve(httpContext.User, out var caller) || caller is null)
        {
            return Results.Unauthorized();
        }

        try
        {
            var entry = await catalog.GetByCnjAsync(cnj, cancellationToken);
            return AuthorizedResult(caller, entry);
        }
        catch (ArgumentException exception)
        {
            return Results.BadRequest(new ApiReadError("invalid_cnj", exception.Message));
        }
    }

    public static async Task<IResult> GetByCaseIdAsync(
        HttpContext httpContext,
        string caseId,
        IProcessSummaryCallerContextResolver callerResolver,
        PostgresProcessCatalog catalog,
        CancellationToken cancellationToken)
    {
        if (!callerResolver.TryResolve(httpContext.User, out var caller) || caller is null)
        {
            return Results.Unauthorized();
        }

        try
        {
            var entry = await catalog.GetByCaseIdAsync(caseId, cancellationToken);
            return AuthorizedResult(caller, entry);
        }
        catch (ArgumentException exception)
        {
            return Results.BadRequest(new ApiReadError("invalid_case_id", exception.Message));
        }
    }

    private static IResult AuthorizedResult(CallerContext caller, ProcessCatalogEntry? entry)
    {
        if (entry is null || !caller.IsAuthorizedFor(new LegalCaseId(entry.CaseId)))
        {
            return Results.NotFound();
        }

        using var document = JsonDocument.Parse(entry.CanonicalJson);
        return Results.Ok(new ProcessLookupResponse(
            entry.CaseId,
            entry.Cnj,
            entry.SourceName,
            document.RootElement.Clone()));
    }
}

public sealed record ProcessLookupResponse(
    string CaseId,
    string Cnj,
    string SourceName,
    JsonElement Process);
