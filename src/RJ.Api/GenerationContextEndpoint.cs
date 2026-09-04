using RJ.Application.Generation;

namespace RJ.Api;

public static class GenerationContextEndpoint
{
    private const string InvalidEvidenceMessage = "Generation context could not be constructed from the retrieved evidence.";

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
            return Results.Ok(context);
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
}

public sealed record GenerationContextError(string Error);
