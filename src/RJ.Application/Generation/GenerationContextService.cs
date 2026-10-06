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

    public async Task<GenerationContext> BuildAuthorizedAsync(
        string caseId,
        string query,
        int retrievalLimit,
        int characterBudget,
        IReadOnlyCollection<string> authorizedEvidenceSourceNames,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(authorizedEvidenceSourceNames);
        var allowedSources = authorizedEvidenceSourceNames.ToHashSet(StringComparer.Ordinal);
        var evidence = await retrieval.RetrieveEvidenceAsync(
            caseId,
            query,
            retrievalLimit,
            cancellationToken);
        var authorizedEvidence = evidence
            .Where(item => allowedSources.Contains(item.SourceName))
            .ToArray();

        return builder.Build(caseId, query, characterBudget, authorizedEvidence);
    }
}
