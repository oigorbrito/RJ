using RJ.Application.Evaluation;
using RJ.Application.Generation;
using RJ.Application.Sources;
using RJ.Domain.Cases;

namespace RJ.Application.Benchmarking;

public sealed class RjudiProcessBenchmarkCatalogFactory(
    ProcessGenerationContextComposer composer,
    IGenerationModel model)
{
    public const string CatalogVersion = "rjudi-process-generation-benchmark-v1";
    public const string CaseId = "case-001-process-summary";

    public async Task<GenerationBenchmarkCatalog> CreateAsync(
        LegalCase legalCase,
        IReadOnlyList<ProcessAttachmentContent> attachmentContents,
        LegalCaseConsistencyReport? consistencyReport,
        string instruction,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(legalCase);
        ArgumentNullException.ThrowIfNull(attachmentContents);

        var context = consistencyReport is null
            ? composer.Compose(legalCase, attachmentContents, ProcessSummaryPrompt.BuildQuery(instruction), 12000)
            : composer.Compose(legalCase, attachmentContents, consistencyReport, ProcessSummaryPrompt.BuildQuery(instruction), 12000);
        var output = await model.GenerateAsync(context, cancellationToken);
        var validation = ProcessSummaryValidator.Validate(legalCase, output);

        if (output.Abstained)
        {
            throw new InvalidOperationException("RJudi process benchmark catalog cannot be built from an abstained deterministic output.");
        }

        if (!validation.IsValid)
        {
            throw new InvalidOperationException("RJudi process benchmark catalog cannot be built from invalid process summary output.");
        }

        var expectedClaims = output.Claims
            .Select(claim => new ExpectedGenerationClaim(claim.Text, claim.Citations))
            .ToArray();
        var evaluationCase = new GenerationEvaluationCase(
            CaseId,
            context,
            expectedClaims,
            false);

        return new GenerationBenchmarkCatalog(CatalogVersion, [evaluationCase]);
    }
}
