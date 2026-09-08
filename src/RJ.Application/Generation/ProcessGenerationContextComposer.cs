using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using RJ.Application.Retrieval;
using RJ.Application.Sources;
using RJ.Domain.Cases;

namespace RJ.Application.Generation;

public sealed class ProcessGenerationContextComposer(GenerationContextBuilder builder)
{
    public const int MaxInlineSteps = 40;

    public GenerationContext Compose(LegalCase legalCase, string query, int characterBudget)
    {
        ArgumentNullException.ThrowIfNull(legalCase);
        ArgumentNullException.ThrowIfNull(builder);

        var evidence = BuildEvidence(legalCase);
        return builder.Build(legalCase.Id.Value, query, characterBudget, evidence);
    }

    public GenerationContext Compose(
        LegalCase legalCase,
        IReadOnlyList<ProcessAttachmentContent> attachmentContents,
        string query,
        int characterBudget)
    {
        ArgumentNullException.ThrowIfNull(legalCase);
        ArgumentNullException.ThrowIfNull(builder);

        var evidence = BuildEvidence(legalCase, attachmentContents);
        return builder.Build(legalCase.Id.Value, query, characterBudget, evidence);
    }

    public GenerationContext Compose(
        LegalCase legalCase,
        IReadOnlyList<ProcessAttachmentContent> attachmentContents,
        LegalCaseConsistencyReport consistencyReport,
        string query,
        int characterBudget)
    {
        ArgumentNullException.ThrowIfNull(legalCase);
        ArgumentNullException.ThrowIfNull(builder);

        var evidence = BuildEvidence(legalCase, attachmentContents, consistencyReport);
        return builder.Build(legalCase.Id.Value, query, characterBudget, evidence);
    }

    public static IReadOnlyList<LegalEvidenceHit> BuildEvidence(LegalCase legalCase)
    {
        return BuildEvidence(legalCase, Array.Empty<ProcessAttachmentContent>());
    }

    public static IReadOnlyList<LegalEvidenceHit> BuildEvidence(
        LegalCase legalCase,
        IReadOnlyList<ProcessAttachmentContent> attachmentContents)
    {
        return BuildEvidence(legalCase, attachmentContents, null);
    }

    public static IReadOnlyList<LegalEvidenceHit> BuildEvidence(
        LegalCase legalCase,
        IReadOnlyList<ProcessAttachmentContent> attachmentContents,
        LegalCaseConsistencyReport? consistencyReport)
    {
        ArgumentNullException.ThrowIfNull(legalCase);
        ArgumentNullException.ThrowIfNull(attachmentContents);

        var documentId = $"{legalCase.Id.Value}:canonical-process";
        var sourceName = PrimarySourceName(legalCase);
        var sections = BuildSections(legalCase, attachmentContents, consistencyReport).ToArray();
        var source = string.Join(Environment.NewLine, sections.Select(section => section.Text));
        var sourceHash = ComputeSha256(source);

        var hits = new List<LegalEvidenceHit>(sections.Length);
        var searchStart = 0;
        for (var index = 0; index < sections.Length; index++)
        {
            var section = sections[index];
            var startOffset = source.IndexOf(section.Text, searchStart, StringComparison.Ordinal);
            if (startOffset < 0)
            {
                throw new InvalidOperationException("Process evidence section was not found in canonical source.");
            }

            hits.Add(new LegalEvidenceHit(
                legalCase.Id.Value,
                documentId,
                sourceName,
                sourceHash,
                section.Text,
                SourcePosition.Create(startOffset, section.Text.Length, source.Length),
                1.0f - (index * 0.001f)));

            searchStart = startOffset + section.Text.Length;
        }

        return hits;
    }

    private static IEnumerable<ProcessEvidenceSection> BuildSections(
        LegalCase legalCase,
        IReadOnlyList<ProcessAttachmentContent> attachmentContents,
        LegalCaseConsistencyReport? consistencyReport)
    {
        if (consistencyReport is not null && !StringComparer.Ordinal.Equals(consistencyReport.Cnj, legalCase.Cnj.Value))
        {
            throw new InvalidOperationException("Consistency report must belong to the composed legal case CNJ.");
        }

        yield return new ProcessEvidenceSection($"CNJ: {legalCase.Cnj.Value}");
        yield return new ProcessEvidenceSection($"Nome: {legalCase.Name}");
        yield return new ProcessEvidenceSection($"Juizo: {legalCase.Court}");
        yield return new ProcessEvidenceSection($"Fase: {legalCase.Phase}");
        yield return new ProcessEvidenceSection($"Status: {legalCase.Status}");

        if (legalCase.Amount.HasValue)
        {
            yield return new ProcessEvidenceSection($"Valor da causa: {legalCase.Amount.Value.ToString("0.##", CultureInfo.InvariantCulture)}");
        }

        foreach (var party in legalCase.Parties)
        {
            var document = string.IsNullOrWhiteSpace(party.MainDocument) ? "sem documento principal" : party.MainDocument;
            yield return new ProcessEvidenceSection($"Parte: {party.Name}; polo: {party.Side}; tipo: {party.PersonType}; documento: {document}");
        }

        foreach (var lawyer in legalCase.Lawyers)
        {
            yield return new ProcessEvidenceSection($"Advogado: {lawyer.Name}; OAB: {lawyer.Oab}");
        }

        foreach (var classification in legalCase.Classifications)
        {
            yield return new ProcessEvidenceSection($"Classe: {classification.Code} - {classification.Name}");
        }

        foreach (var subject in legalCase.Subjects)
        {
            yield return new ProcessEvidenceSection($"Assunto: {subject.Code} - {subject.Name}");
        }

        foreach (var step in legalCase.Steps.Take(MaxInlineSteps))
        {
            var date = ProcessNormalization.FormatSaoPauloDateTime(step.Date);
            var content = ProcessNormalization.NormalizeStepContent(step.Content);
            yield return new ProcessEvidenceSection($"Movimentacao: {date}; id: {step.Id}; {content}");
        }

        foreach (var attachment in legalCase.Attachments)
        {
            var date = ProcessNormalization.FormatSaoPauloDateTime(attachment.Date);
            var content = attachmentContents.FirstOrDefault(item => StringComparer.Ordinal.Equals(item.AttachmentId, attachment.Id));
            var contentStatus = content is null ? "conteudo: nao observado" : $"conteudo_sha256: {content.ContentSha256}";
            yield return new ProcessEvidenceSection($"Anexo metadata: {date}; id: {attachment.Id}; nome: {attachment.Name}; status: {attachment.Status}; extensao: {attachment.Extension}; {contentStatus}");
        }

        if (consistencyReport is not null)
        {
            foreach (var inconsistency in consistencyReport.Inconsistencies.OrderBy(item => item.FieldPath, StringComparer.Ordinal))
            {
                var observations = string.Join(
                    " | ",
                    inconsistency.Observations
                        .OrderBy(item => item.Value, StringComparer.Ordinal)
                        .ThenBy(item => item.SourceName, StringComparer.Ordinal)
                        .Select(item => $"{item.SourceName} em {item.ObservedPath}: {item.Value}"));
                yield return new ProcessEvidenceSection($"Inconsistencia: campo: {inconsistency.FieldPath}; observacoes: {observations}");
            }
        }

        foreach (var content in attachmentContents)
        {
            if (!StringComparer.Ordinal.Equals(content.CaseId, legalCase.Id.Value))
            {
                throw new InvalidOperationException("Attachment content must belong to the composed legal case.");
            }

            if (legalCase.Attachments.All(attachment => !StringComparer.Ordinal.Equals(attachment.Id, content.AttachmentId)))
            {
                throw new InvalidOperationException("Attachment content must reference an observed attachment metadata record.");
            }

            foreach (var chunk in ProcessAttachmentChunker.Chunk(content))
            {
                yield return new ProcessEvidenceSection(
                    $"Anexo conteudo observado: id: {chunk.AttachmentId}; sha256: {chunk.ContentSha256}; chunk: {chunk.ChunkIndex}; token_start: {chunk.TokenStart}; token_count: {chunk.TokenCount}; texto: {ProcessNormalization.NormalizeStepContent(chunk.Text)}");
            }
        }
    }

    private static string PrimarySourceName(LegalCase legalCase) =>
        legalCase.Provenance
            .OrderBy(item => item.FieldPath, StringComparer.Ordinal)
            .Select(item => item.SourceName)
            .First();

    private static string ComputeSha256(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    private sealed record ProcessEvidenceSection(string Text);
}
