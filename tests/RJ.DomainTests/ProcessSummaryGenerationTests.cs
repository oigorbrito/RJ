using System.Globalization;
using RJ.Application.Generation;
using RJ.Application.Sources;
using RJ.Domain.Cases;

namespace RJ.DomainTests;

public sealed class ProcessSummaryGenerationTests
{
    private static readonly string RepoRoot = FindRepoRoot();
    private static readonly string FixturePath = Path.Combine(RepoRoot, "tests", "fixtures", "rj", "response_60031603620268160021_1.json");
    private static readonly DateTimeOffset ObservedAt =
        DateTimeOffset.Parse("2026-09-02T18:51:04.800Z", CultureInfo.InvariantCulture);

    [Fact]
    public void Process_summary_prompt_is_versioned_and_contains_required_sections()
    {
        var query = ProcessSummaryPrompt.BuildQuery("Resuma o processo.");

        Assert.Contains("PromptId: rjudi-process-summary", query, StringComparison.Ordinal);
        Assert.Contains("PromptVersion: rjudi-process-summary-v1", query, StringComparison.Ordinal);
        Assert.Contains("pontos_de_atencao", query, StringComparison.Ordinal);
        Assert.Contains("source inconsistencies", query, StringComparison.Ordinal);
        Assert.Contains("do not invent attachment content", query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Deterministic_fake_returns_same_output_for_same_prompt_and_context()
    {
        var context = BuildContext();
        var model = new DeterministicProcessSummaryModel();

        var first = await model.GenerateAsync(context, CancellationToken.None);
        var second = await model.GenerateAsync(context, CancellationToken.None);

        Assert.False(first.Abstained);
        Assert.Equal(first.Claims.Count, second.Claims.Count);
        Assert.Equal(first.Claims.Select(claim => claim.Text), second.Claims.Select(claim => claim.Text));
        Assert.Equal(
            first.Claims.SelectMany(claim => claim.Citations),
            second.Claims.SelectMany(claim => claim.Citations));
        Assert.Contains(first.Claims, claim => claim.Text.StartsWith("CNJ:", StringComparison.Ordinal));
        Assert.Contains(first.Claims, claim => claim.Text.StartsWith("Juizo:", StringComparison.Ordinal));
        Assert.Contains(first.Claims, claim => claim.Text.StartsWith("Valor da causa:", StringComparison.Ordinal));
        Assert.Contains(first.Claims, claim => claim.Text.StartsWith("Advogado:", StringComparison.Ordinal));
        Assert.Contains(first.Claims, claim => claim.Text.StartsWith("Sigilo:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Generation_service_accepts_deterministic_process_claims_with_exact_context_citations()
    {
        var context = BuildContext();
        var service = new GenerationService(new DeterministicProcessSummaryModel());

        var output = await service.GenerateAsync(context, CancellationToken.None);

        Assert.False(output.Abstained);
        Assert.NotEmpty(output.Claims);
        Assert.All(output.Claims, claim => Assert.All(claim.Citations, citation =>
            Assert.Contains(context.Items, item =>
                item.DocumentId == citation.DocumentId
                && item.ContentSha256 == citation.ContentSha256
                && item.Position.StartOffset == citation.StartOffset
                && item.Position.Length == citation.Length)));
        Assert.DoesNotContain(output.Claims, claim => claim.Text.Contains("02727135971", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Deterministic_process_e2e_canonicalizes_composes_generates_and_validates_case_001_without_retrieval()
    {
        var legalCase = LoadCanonicalCase();
        var composer = new ProcessGenerationContextComposer(new GenerationContextBuilder());
        var context = composer.Compose(legalCase, ProcessSummaryPrompt.BuildQuery("Resuma o processo."), 12000);
        var service = new GenerationService(new DeterministicProcessSummaryModel());

        var output = await service.GenerateAsync(context, CancellationToken.None);
        var validation = ProcessSummaryValidator.Validate(legalCase, output);

        Assert.False(output.Abstained);
        Assert.True(validation.IsValid);
        Assert.Empty(validation.Errors);
        Assert.Equal(13, context.Items.Count(item => item.Excerpt.StartsWith("Movimentacao:", StringComparison.Ordinal)));
        Assert.Contains(context.Items, item => item.Excerpt.Contains("conteudo: nao observado", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Deterministic_process_e2e_preserves_and_cites_consistency_inconsistencies()
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
        var composer = new ProcessGenerationContextComposer(new GenerationContextBuilder());
        var context = composer.Compose(legalCase, [], consistency, ProcessSummaryPrompt.BuildQuery("Resuma o processo."), 12000);
        var service = new GenerationService(new DeterministicProcessSummaryModel());

        var output = await service.GenerateAsync(context, CancellationToken.None);
        var validation = ProcessSummaryValidator.Validate(legalCase, output);

        Assert.False(output.Abstained);
        Assert.True(validation.IsValid);
        var claim = Assert.Single(output.Claims, item => item.Text.StartsWith("Inconsistencia:", StringComparison.Ordinal));
        var citation = Assert.Single(claim.Citations);
        Assert.Contains(context.Items, item =>
            item.DocumentId == citation.DocumentId
            && item.ContentSha256 == citation.ContentSha256
            && item.Position.StartOffset == citation.StartOffset
            && item.Position.Length == citation.Length);
    }

    [Fact]
    public async Task Deterministic_fake_abstains_when_prompt_identity_is_missing()
    {
        var context = BuildContext("Resuma sem identidade de prompt");
        var service = new GenerationService(new DeterministicProcessSummaryModel());

        var output = await service.GenerateAsync(context, CancellationToken.None);

        Assert.True(output.Abstained);
        Assert.Equal("Process summary prompt identity is missing.", output.AbstentionReason);
    }

    private static GenerationContext BuildContext(string? query = null)
    {
        var legalCase = LoadCanonicalCase();
        var composer = new ProcessGenerationContextComposer(new GenerationContextBuilder());

        return composer.Compose(
            legalCase,
            query ?? ProcessSummaryPrompt.BuildQuery("Resuma o processo."),
            12000);
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
