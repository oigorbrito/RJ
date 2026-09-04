using Npgsql;
using RJ.Application.Ingestion;
using RJ.Domain.Documents;

namespace RJ.Infrastructure.Persistence;

public sealed class PostgresLegalDocumentWriter(NpgsqlDataSource dataSource) : ILegalDocumentWriter
{
    private readonly NpgsqlDataSource _dataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));

    public async Task StoreAsync(LegalDocument document, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        const string insertSql = """
            INSERT INTO legal_documents (
                case_id,
                document_id,
                source_name,
                raw_content,
                content,
                content_sha256)
            VALUES (
                @case_id,
                @document_id,
                @source_name,
                @raw_content,
                @content,
                @content_sha256)
            ON CONFLICT DO NOTHING;
            """;

        await using (var insert = new NpgsqlCommand(insertSql, connection, transaction)
        {
            CommandTimeout = PostgresCommandPolicy.CommandTimeoutSeconds
        })
        {
            insert.Parameters.AddWithValue("case_id", document.CaseId.Value);
            insert.Parameters.AddWithValue("document_id", document.Id.Value);
            insert.Parameters.AddWithValue("source_name", document.SourceName);
            insert.Parameters.AddWithValue("raw_content", document.RawContent);
            insert.Parameters.AddWithValue("content", document.Content);
            insert.Parameters.AddWithValue("content_sha256", document.ContentSha256);

            var inserted = await insert.ExecuteNonQueryAsync(cancellationToken);
            if (inserted == 1)
            {
                await transaction.CommitAsync(cancellationToken);
                return;
            }
        }

        const string conflictSql = """
            SELECT document_id, content_sha256
            FROM legal_documents
            WHERE case_id = @case_id
              AND (document_id = @document_id OR content_sha256 = @content_sha256)
            ORDER BY CASE WHEN document_id = @document_id THEN 0 ELSE 1 END
            LIMIT 1;
            """;

        await using var conflict = new NpgsqlCommand(conflictSql, connection, transaction)
        {
            CommandTimeout = PostgresCommandPolicy.CommandTimeoutSeconds
        };
        conflict.Parameters.AddWithValue("case_id", document.CaseId.Value);
        conflict.Parameters.AddWithValue("document_id", document.Id.Value);
        conflict.Parameters.AddWithValue("content_sha256", document.ContentSha256);

        await using var reader = await conflict.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException("PostgreSQL reported an insert conflict but no conflicting legal document was observable.");
        }

        var existingDocumentId = reader.GetString(0);
        var existingContentSha256 = reader.GetString(1).TrimEnd();
        await reader.CloseAsync();

        if (StringComparer.Ordinal.Equals(existingDocumentId, document.Id.Value)
            && StringComparer.Ordinal.Equals(existingContentSha256, document.ContentSha256))
        {
            await transaction.CommitAsync(cancellationToken);
            return;
        }

        throw new LegalDocumentConflictException(
            $"Legal document conflict for case '{document.CaseId.Value}' and document '{document.Id.Value}'.");
    }
}
