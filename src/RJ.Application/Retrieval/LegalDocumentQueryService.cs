using RJ.Domain.Cases;
using RJ.Domain.Documents;

namespace RJ.Application.Retrieval;

public sealed class LegalDocumentQueryService(
    ILegalDocumentReader reader,
    ILegalDocumentSearch search)
{
    private const int ExcerptContext = 160;

    public Task<LegalDocumentSnapshot?> GetAsync(
        string caseId,
        string documentId,
        CancellationToken cancellationToken)
    {
        return reader.GetAsync(
            new LegalCaseId(caseId),
            new LegalDocumentId(documentId),
            cancellationToken);
    }

    public Task<IReadOnlyList<LegalDocumentSnapshot>> ListAsync(
        string caseId,
        CancellationToken cancellationToken)
    {
        return reader.ListByCaseAsync(new LegalCaseId(caseId), cancellationToken);
    }

    public Task<IReadOnlyList<LegalDocumentSearchHit>> SearchAsync(
        string caseId,
        string query,
        int limit,
        CancellationToken cancellationToken)
    {
        return search.SearchAsync(new LegalCaseId(caseId), query, limit, cancellationToken);
    }

    public async Task<IReadOnlyList<LegalEvidenceHit>> RetrieveEvidenceAsync(
        string caseId,
        string query,
        int limit,
        CancellationToken cancellationToken)
    {
        var hits = await SearchAsync(caseId, query, limit, cancellationToken);
        var evidence = new List<LegalEvidenceHit>(hits.Count);

        foreach (var hit in hits)
        {
            var located = LocateExcerpt(hit.Document.RawContent, query);
            if (located is null)
            {
                continue;
            }

            evidence.Add(new LegalEvidenceHit(
                hit.Document.CaseId,
                hit.Document.DocumentId,
                hit.Document.SourceName,
                hit.Document.ContentSha256,
                located.Value.Excerpt,
                located.Value.Position,
                hit.Rank));
        }

        return evidence;
    }

    private static (string Excerpt, SourcePosition Position)? LocateExcerpt(string rawContent, string query)
    {
        if (string.IsNullOrWhiteSpace(rawContent) || string.IsNullOrWhiteSpace(query))
        {
            return null;
        }

        var needle = query.Trim();
        var matchIndex = rawContent.IndexOf(needle, StringComparison.OrdinalIgnoreCase);
        var matchLength = needle.Length;

        if (matchIndex < 0)
        {
            var token = ExtractSearchTokens(needle)
                .OrderByDescending(value => value.Length)
                .ThenBy(value => value, StringComparer.Ordinal)
                .FirstOrDefault(value => rawContent.Contains(value, StringComparison.OrdinalIgnoreCase));

            if (token is null)
            {
                return null;
            }

            matchIndex = rawContent.IndexOf(token, StringComparison.OrdinalIgnoreCase);
            matchLength = token.Length;
        }

        var start = Math.Max(0, matchIndex - ExcerptContext);
        var end = Math.Min(rawContent.Length, matchIndex + matchLength + ExcerptContext);
        var length = end - start;
        var position = SourcePosition.Create(start, length, rawContent.Length);
        return (rawContent.Substring(start, length), position);
    }

    private static IEnumerable<string> ExtractSearchTokens(string query)
    {
        var token = new System.Text.StringBuilder();

        foreach (var character in query)
        {
            if (char.IsLetterOrDigit(character))
            {
                token.Append(character);
                continue;
            }

            if (token.Length > 1)
            {
                yield return token.ToString();
            }

            token.Clear();
        }

        if (token.Length > 1)
        {
            yield return token.ToString();
        }
    }
}
