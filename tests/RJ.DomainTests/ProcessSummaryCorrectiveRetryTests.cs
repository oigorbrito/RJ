using System.Globalization;
using RJ.Application.Generation;
using RJ.Application.Sources;

namespace RJ.DomainTests;

public sealed class ProcessSummaryCorrectiveRetryTests
{
    private static readonly string RepoRoot = FindRepoRoot();
    private static readonly string FixturePath = Path.Combine(RepoRoot, "tests", "fixtures", "rj", "response_60031603620268160021_1.json");
    private static readonly DateTimeOffset ObservedAt =
        DateTimeOffset.Parse("2026-09-02T18:51:04.800Z", CultureInfo.InvariantCulture);

    [Fact]
    public async Task GenerateAsync_returns_first_output_when_process_validation_passes()
    {
        var legalCase = LoadCanonicalCase();
        var context = BuildContext(legalCase);

        var result = await ProcessSummaryCorrectiveRetry.GenerateAsync(
            new GenerationService(new DeterministicProcessSummaryModel()),
            legalCase,
            context,
            null,
            CancellationToken.None);

        Assert.False(result.Retried);
        Assert.Equal(1, result.Attempts);
        Assert.True(result.Validation.IsValid);
    }

    [Fact]
    public async Task GenerateAsync_retries_once_with_sanitized_validator_errors()
    {
        var legalCase = LoadCanonicalCase();
        var context = BuildContext(legalCase);
        var model = new FirstAttemptIncompleteThenDeterministicModel();

        var result = await ProcessSummaryCorrectiveRetry.GenerateAsync(
            new GenerationService(model),
            legalCase,
            context,
            null,
            CancellationToken.None);

        Assert.True(result.Retried);
        Assert.Equal(2, result.Attempts);
        Assert.True(result.Validation.IsValid);
        Assert.Contains("CorrectiveRetry:", model.SecondQuery, StringComparison.Ordinal);
        Assert.Contains("Summary does not mention the canonical case name.", model.SecondQuery, StringComparison.Ordinal);
        Assert.DoesNotContain("02727135971", model.SecondQuery, StringComparison.Ordinal);
    }

    private static RJ.Domain.Cases.LegalCase LoadCanonicalCase()
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

    private static GenerationContext BuildContext(RJ.Domain.Cases.LegalCase legalCase) =>
        new ProcessGenerationContextComposer(new GenerationContextBuilder())
            .Compose(legalCase, ProcessSummaryPrompt.BuildQuery("Resuma o processo."), 12000);

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

    private sealed class FirstAttemptIncompleteThenDeterministicModel : IGenerationModel
    {
        private readonly DeterministicProcessSummaryModel deterministic = new();
        private int calls;

        public string SecondQuery { get; private set; } = string.Empty;

        public Task<GenerationModelOutput> GenerateAsync(GenerationContext context, CancellationToken cancellationToken)
        {
            calls++;
            if (calls == 1)
            {
                var cnj = context.Items.First(item => item.Excerpt.StartsWith("CNJ:", StringComparison.Ordinal));
                return Task.FromResult(new GenerationModelOutput(
                    false,
                    null,
                    [
                        new GenerationClaim(
                            cnj.Excerpt,
                            [new GenerationCitation(cnj.DocumentId, cnj.ContentSha256, cnj.Position.StartOffset, cnj.Position.Length)])
                    ]));
            }

            SecondQuery = context.Query;
            return deterministic.GenerateAsync(context, cancellationToken);
        }
    }
}
