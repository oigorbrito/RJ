using System.Security.Cryptography;
using System.Text;
using RJ.Domain.Cases;
using RJ.Domain.Documents;

namespace RJ.Application.Ingestion;

public sealed class IngestLegalDocumentHandler(ILegalDocumentWriter writer)
{
    public Task HandleAsync(IngestLegalDocumentCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var normalizedContent = Normalize(command.RawContent);
        var contentSha256 = ComputeSha256(command.RawContent);

        var document = new LegalDocument(
            new LegalDocumentId(command.DocumentId),
            new LegalCaseId(command.CaseId),
            command.SourceName,
            command.RawContent,
            normalizedContent,
            contentSha256);

        return writer.StoreAsync(document, cancellationToken);
    }

    private static string Normalize(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return value.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
    }

    private static string ComputeSha256(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var bytes = Encoding.UTF8.GetBytes(value);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexStringLower(hash);
    }
}
