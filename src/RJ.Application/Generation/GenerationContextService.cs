using RJ.Application.Retrieval;

namespace RJ.Application.Generation;

public sealed class GenerationContextService(
    LegalDocumentQueryService retrieval,
    GenerationContextBuilder builder)
{
    public async Task<GenerationContext> BuildAsync(
        string caseId,
        string query,
        int retrievalLimit,
        int characterBudget,
        CancellationToken cancellationToken)
    {
        var evidence = await retrieval.RetrieveEvidenceAsync(
            caseId,
            query,
            retrievalLimit,
            cancellationToken);

        return builder.Build(caseId, query, characterBudget, evidence);
    }
}
