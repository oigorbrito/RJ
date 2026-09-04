using Npgsql;
using RJ.Application.Retrieval;
using RJ.Domain.Cases;

namespace RJ.Infrastructure.Persistence;

public sealed class PostgresLegalDocumentSearch(NpgsqlDataSource dataSource) : ILegalDocumentSearch
{
    public async Task<IReadOnlyList<LegalDocumentSearchHit>> SearchAsync(
        LegalCaseId caseId,
        string query,
        int limit,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            throw new ArgumentException("Search query cannot be empty.", nameof(query));
        }

        if (limit is < 1 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(limit), limit, "Limit must be between 1 and 100.");
        }

        const string sql = """
            WITH q AS (SELECT websearch_to_tsquery('portuguese', $2) AS query)
            SELECT d.case_id, d.document_id, d.source_name, d.content, d.content_sha256,
                   ts_rank_cd(d.search_vector, q.query) AS rank
            FROM legal_documents d
            CROSS JOIN q
            WHERE d.case_id = $1
              AND d.search_vector @@ q.query
            ORDER BY rank DESC, d.document_id ASC
            LIMIT $3;
            """;

        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue(caseId.Value);
        command.Parameters.AddWithValue(query.Trim());
        command.Parameters.AddWithValue(limit);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var hits = new List<LegalDocumentSearchHit>();
        while (await reader.ReadAsync(cancellationToken))
        {
            var snapshot = new LegalDocumentSnapshot(
                reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4));
            hits.Add(new LegalDocumentSearchHit(snapshot, reader.GetFloat(5)));
        }

        return hits;
    }
}
