using System.Globalization;
using RJ.Application.Generation;
using RJ.Application.Sources;
using RJ.Domain.Cases;

namespace RJ.DomainTests;

public sealed class ProcessGenerationContextComposerTests
{
    private static readonly string RepoRoot = FindRepoRoot();
    private static readonly string FixturePath = Path.Combine(RepoRoot, "tests", "fixtures", "rj", "response_60031603620268160021_1.json");
    private static readonly DateTimeOffset ObservedAt =
        DateTimeOffset.Parse("2026-09-02T18:51:04.800Z", CultureInfo.InvariantCulture);

    [Fact]
    public void Build_evidence_inlines_all_case_001_steps_and_marks_unobserved_attachment_content()
    {
        var legalCase = LoadCanonicalCase();

        var evidence = ProcessGenerationContextComposer.BuildEvidence(legalCase);

        Assert.Equal(13, evidence.Count(item => item.Excerpt.StartsWith("Movimentacao:", StringComparison.Ordinal)));
        Assert.Contains(evidence, item => item.Excerpt.Contains("03/03/2027 15:00", StringComparison.Ordinal));
        Assert.Contains(evidence, item => item.Excerpt.Contains("conteudo: nao observado", StringComparison.Ordinal));
        Assert.All(evidence, item => Assert.Equal($"{legalCase.Id.Value}:canonical-process", item.DocumentId));
        Assert.All(evidence, item => Assert.Equal(64, item.ContentSha256.Length));
        Assert.All(evidence, item => Assert.Equal(item.Excerpt.Length, item.Position.Length));
    }

    [Fact]
    public void Compose_uses_existing_generation_context_builder_without_retrieval()
    {
        var legalCase = LoadCanonicalCase();
        var composer = new ProcessGenerationContextComposer(new GenerationContextBuilder());

        var context = composer.Compose(legalCase, "Resuma o processo", 12000);

        Assert.Equal(legalCase.Id.Value, context.CaseId);
        Assert.Equal("Resuma o processo", context.Query);
        Assert.NotEmpty(context.Items);
        Assert.Contains(context.Items, item => item.Excerpt.StartsWith("CNJ:", StringComparison.Ordinal));
        Assert.DoesNotContain(context.Items, item => item.Excerpt.Contains("02727135971", StringComparison.Ordinal));
        Assert.Contains(context.Items, item => item.Excerpt.Contains("***.271.359-**", StringComparison.Ordinal));
    }

    [Fact]
    public void Build_evidence_limits_steps_to_forty()
    {
        var legalCase = LoadCanonicalCase();
        var manySteps = Enumerable.Range(0, 45)
            .Select(index => new LegalCaseStep($"step-{index:00}", ObservedAt.AddMinutes(index), $"step content {index}", "source"))
            .ToArray();
        var expanded = new LegalCase(
            legalCase.Id,
            legalCase.Cnj,
            legalCase.Name,
            legalCase.Court,
            legalCase.Phase,
            legalCase.Status,
            legalCase.SecrecyLevel,
            legalCase.Amount,
            legalCase.Parties,
            legalCase.Lawyers,
            legalCase.Classifications,
            legalCase.Subjects,
            manySteps,
            Array.Empty<LegalCaseAttachment>(),
            legalCase.Provenance);

        var evidence = ProcessGenerationContextComposer.BuildEvidence(expanded);

        Assert.Equal(40, evidence.Count(item => item.Excerpt.StartsWith("Movimentacao:", StringComparison.Ordinal)));
    }

    [Fact]
    public void Build_evidence_includes_attachment_content_only_when_observed()
    {
        var legalCase = LoadCanonicalCase();
        var content = new ProcessAttachmentContent(
            legalCase.Id.Value,
            legalCase.Attachments[0].Id,
            "attachment-extractor",
            "attachments/411788364428621657023616086781.html",
            "Texto extraido do anexo observado.",
            ObservedAt);

        var evidence = ProcessGenerationContextComposer.BuildEvidence(legalCase, [content]);

        Assert.Contains(evidence, item => item.Excerpt.Contains($"conteudo_sha256: {content.ContentSha256}", StringComparison.Ordinal));
        Assert.Contains(evidence, item => item.Excerpt.Contains("Anexo conteudo observado:", StringComparison.Ordinal));
        Assert.Contains(evidence, item => item.Excerpt.Contains("Texto extraido do anexo observado.", StringComparison.Ordinal));
    }

    [Fact]
    public void Build_evidence_can_inline_deterministic_consistency_inconsistencies()
    {
        var legalCase = LoadCanonicalCase();
        var dataJudCase = new LegalCase(
            new LegalCaseId("datajud-case"),
            legalCase.Cnj,
            legalCase.Name,
            legalCase.Court,
            legalCase.Phase,
            "BAIXADO",
            legalCase.SecrecyLevel,
            legalCase.Amount,
            legalCase.Parties,
            legalCase.Lawyers,
            legalCase.Classifications,
            legalCase.Subjects,
            legalCase.Steps,
            legalCase.Attachments,
            new[]
            {
                new LegalCaseFieldProvenance(
                    "status",
                    "DataJud",
                    "datajud/60031603620268160021.json",
                    "b5decf20e6bb330a7a58974711b7ec8e72215d74568e014e8cf1ced14ee916aa",
                    "status",
                    ObservedAt)
            });
        var consistency = LegalCaseConsistencyEngine.Analyze(new[] { legalCase, dataJudCase });

        var evidence = ProcessGenerationContextComposer.BuildEvidence(legalCase, [], consistency);

        var inconsistency = Assert.Single(evidence, item => item.Excerpt.StartsWith("Inconsistencia:", StringComparison.Ordinal));
        Assert.Contains("campo: status", inconsistency.Excerpt, StringComparison.Ordinal);
        Assert.Contains("DataJud em status: BAIXADO", inconsistency.Excerpt, StringComparison.Ordinal);
        Assert.Contains("ATIVO", inconsistency.Excerpt, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_evidence_rejects_attachment_content_for_other_case_or_unknown_attachment()
    {
        var legalCase = LoadCanonicalCase();
        var otherCase = new ProcessAttachmentContent(
            "other-case",
            legalCase.Attachments[0].Id,
            "attachment-extractor",
            "attachments/other.html",
            "Texto observado.",
            ObservedAt);
        var unknownAttachment = new ProcessAttachmentContent(
            legalCase.Id.Value,
            "unknown-attachment",
            "attachment-extractor",
            "attachments/unknown.html",
            "Texto observado.",
            ObservedAt);

        Assert.Throws<InvalidOperationException>(() => ProcessGenerationContextComposer.BuildEvidence(legalCase, [otherCase]));
        Assert.Throws<InvalidOperationException>(() => ProcessGenerationContextComposer.BuildEvidence(legalCase, [unknownAttachment]));
    }

    [Fact]
    public void Build_evidence_rejects_consistency_report_for_other_cnj()
    {
        var legalCase = LoadCanonicalCase();
        var report = new LegalCaseConsistencyReport("5003160-53.2026.8.16.0021", []);

        Assert.Throws<InvalidOperationException>(() => ProcessGenerationContextComposer.BuildEvidence(legalCase, [], report));
    }

    private static LegalCase LoadCanonicalCase()
    {
        var rawContent = File.ReadAllText(FixturePath);
        var source = new ProcessSourceDocument(
            JuditProcessSourceAdapter.JuditSourceSystem,
            "Judit",
            "tests/fixtures/rj/response_60031603620268160021_1.json",
            rawContent,
            ObservedAt);

        return new JuditProcessSourceAdapter().Canonicalize(source);
    }

    private static string FindRepoRoot()
    {
        var current = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(current, "RJ.slnx")))
        {
            var parent = Directory.GetParent(current) ?? throw new InvalidOperationException("Repository root not found.");
            current = parent.FullName;
        }

        return current;
    }
}
