using Npgsql;
using RJ.Application.Retrieval;
using RJ.Domain.Cases;
using RJ.Domain.Documents;

namespace RJ.Infrastructure.Persistence;

public sealed class PostgresLegalDocumentReader(NpgsqlDataSource dataSource) : ILegalDocumentReader
{
    public async Task<LegalDocumentSnapshot?> GetAsync(
        LegalCaseId caseId,
        LegalDocumentId documentId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT case_id, document_id, source_name, content, content_sha256
            FROM legal_documents
            WHERE case_id = $1 AND document_id = $2;
            """;

        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue(caseId.Value);
        command.Parameters.AddWithValue(documentId.Value);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return ReadSnapshot(reader);
    }

    public async Task<IReadOnlyList<LegalDocumentSnapshot>> ListByCaseAsync(
        LegalCaseId caseId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT case_id, document_id, source_name, content, content_sha256
            FROM legal_documents
            WHERE case_id = $1
            ORDER BY document_id ASC;
            """;

        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue(caseId.Value);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var results = new List<LegalDocumentSnapshot>();

        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(ReadSnapshot(reader));
        }

        return results;
    }

    private static LegalDocumentSnapshot ReadSnapshot(NpgsqlDataReader reader) => new(
        reader.GetString(0),
        reader.GetString(1),
        reader.GetString(2),
        reader.GetString(3),
        reader.GetString(4));
}
