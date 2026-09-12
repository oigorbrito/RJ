using RJ.Domain.Cases;

namespace RJ.Application.Sources;

public static class ProcessAttachmentContentAdmission
{
    public static IReadOnlyList<ProcessAttachmentContent> Admit(
        LegalCase legalCase,
        IReadOnlyList<ProcessAttachmentContent> contents)
    {
        ArgumentNullException.ThrowIfNull(legalCase);
        ArgumentNullException.ThrowIfNull(contents);

        var attachmentIds = legalCase.Attachments
            .Select(item => item.Id)
            .ToHashSet(StringComparer.Ordinal);
        var admitted = new List<ProcessAttachmentContent>(contents.Count);

        foreach (var content in contents)
        {
            ArgumentNullException.ThrowIfNull(content);

            if (!StringComparer.Ordinal.Equals(content.CaseId, legalCase.Id.Value))
            {
                throw new InvalidOperationException("Attachment content must belong to the canonical legal case.");
            }

            if (!attachmentIds.Contains(content.AttachmentId))
            {
                throw new InvalidOperationException("Attachment content must reference observed attachment metadata.");
            }

            admitted.Add(content);
        }

        return admitted
            .OrderBy(item => item.AttachmentId, StringComparer.Ordinal)
            .ThenBy(item => item.SourceName, StringComparer.Ordinal)
            .ThenBy(item => item.SourceReference, StringComparer.Ordinal)
            .ToArray();
    }
}
