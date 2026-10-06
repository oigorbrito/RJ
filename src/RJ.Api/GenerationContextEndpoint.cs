using RJ.Application.Generation;

namespace RJ.Api;

public static class GenerationContextEndpoint
{
    private const string InvalidEvidenceMessage = "Generation context could not be constructed from the retrieved evidence.";

    public static async Task<IResult> HandleAuthorizedAsync(
        HttpContext httpContext,
        string caseId,
        string? q,
        int? limit,
        int? budget,
        IProcessSummaryCallerContextResolver callerResolver,
        GenerationContextService service,
        CancellationToken cancellationToken)
    {
        if (!callerResolver.TryResolve(httpContext.User, out var caller) || caller is null)
        {
            return Results.Unauthorized();
        }

        try
        {
            var authorizedSources = caller.AuthorizedEvidenceSourceNames ?? [];
            if (authorizedSources.Count == 0)
            {
                return Results.NotFound();
            }

            var context = await service.BuildAuthorizedAsync(
                caseId,
                q ?? string.Empty,
                limit ?? 20,
                budget ?? 12000,
                authorizedSources,
                cancellationToken);
            return Results.Ok(ToResponse(context));
        }
        catch (ArgumentException exception)
        {
            return Results.BadRequest(new GenerationContextError(exception.Message));
        }
        catch (InvalidOperationException)
        {
            return Results.UnprocessableEntity(new GenerationContextError(InvalidEvidenceMessage));
        }
    }

    public static async Task<IResult> HandleAsync(
        string caseId,
        string? q,
        int? limit,
        int? budget,
        GenerationContextService service,
        CancellationToken cancellationToken)
    {
        try
        {
            var context = await service.BuildAsync(
                caseId,
                q ?? string.Empty,
                limit ?? 20,
                budget ?? 12000,
                cancellationToken);
            return Results.Ok(ToResponse(context));
        }
        catch (ArgumentException exception)
        {
            return Results.BadRequest(new GenerationContextError(exception.Message));
        }
        catch (InvalidOperationException)
        {
            return Results.UnprocessableEntity(new GenerationContextError(InvalidEvidenceMessage));
        }
    }

    private static GenerationContextResponse ToResponse(GenerationContext context) => new(
        context.CaseId,
        context.Query,
        context.CharacterBudget,
        context.UsedCharacters,
        context.Items.Select(item => new GenerationContextItemResponse(
            item.CaseId,
            item.DocumentId,
            item.SourceName,
            item.ContentSha256,
            item.Excerpt,
            new GenerationContextPosition(item.Position.StartOffset, item.Position.Length),
            item.Rank)).ToArray());
}

public sealed record GenerationContextResponse(
    string CaseId,
    string Query,
    int CharacterBudget,
    int UsedCharacters,
    IReadOnlyList<GenerationContextItemResponse> Items);

public sealed record GenerationContextItemResponse(
    string CaseId,
    string DocumentId,
    string SourceName,
    string ContentSha256,
    string Excerpt,
    GenerationContextPosition Position,
    float Rank);

public sealed record GenerationContextPosition(int StartOffset, int Length);

public sealed record GenerationContextError(string Error);
