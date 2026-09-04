using RJ.Application.Evaluation;
using RJ.Application.Generation;
using RJ.Application.Retrieval;

namespace RJ.DomainTests;

public sealed class GenerationEvaluatorTests
{
    [Fact]
    public void Evaluate_passes_exact_grounded_cited_claim()
    {
        var fixture = CreateAnsweredFixture();
        var expected = Assert.Single(fixture.ExpectedClaims);
        var output = new GenerationModelOutput(
            false,
            null,
            [new GenerationClaim(expected.Text, expected.Citations)]);

        var result = new GenerationEvaluator().Evaluate(fixture, output);

        Assert.True(result.Passed);
        Assert.Equal(1.0, result.ClaimRecall);
        Assert.Equal(1.0, result.CitationValidity);
        Assert.Equal(1.0, result.Groundedness);
    }

    [Fact]
    public void Evaluate_fails_non_compensably_when_citation_is_invalid()
    {
        var fixture = CreateAnsweredFixture();
        var output = new GenerationModelOutput(
            false,
            null,
            [new GenerationClaim(
                "A tutela provisoria foi deferida.",
                [new GenerationCitation("doc-x", new string('f', 64), 0, 10)])]);

        var result = new GenerationEvaluator().Evaluate(fixture, output);

        Assert.False(result.Passed);
        Assert.Equal(0.0, result.CitationValidity);
        Assert.Equal(0.0, result.Groundedness);
    }

    [Fact]
    public void Evaluate_fails_when_expected_claim_is_missing_even_if_existing_claim_is_valid()
    {
        var fixture = CreateTwoClaimFixture();
        var first = fixture.ExpectedClaims[0];
        var output = new GenerationModelOutput(false, null, [new GenerationClaim(first.Text, first.Citations)]);

        var result = new GenerationEvaluator().Evaluate(fixture, output);

        Assert.False(result.Passed);
        Assert.Equal(0.5, result.ClaimRecall);
        Assert.Equal(1.0, result.CitationValidity);
        Assert.Equal(1.0, result.Groundedness);
    }

    [Fact]
    public void Evaluate_passes_expected_abstention_only_when_explicit_and_reasoned()
    {
        var fixture = new GenerationEvaluationCase(
            "abstain-1",
            CreateContext(),
            [],
            true);

        var result = new GenerationEvaluator().Evaluate(
            fixture,
            new GenerationModelOutput(true, "insufficient evidence", []));

        Assert.True(result.Passed);
        Assert.True(result.Abstained);
    }

    private static GenerationEvaluationCase CreateAnsweredFixture()
    {
        var context = CreateContext();
        var item = Assert.Single(context.Items);
        var citation = new GenerationCitation(
            item.DocumentId,
            item.ContentSha256,
            item.Position.StartOffset,
            item.Position.Length);

        return new GenerationEvaluationCase(
            "answered-1",
            context,
            [new ExpectedGenerationClaim("A tutela provisoria foi deferida.", [citation])],
            false);
    }

    private static GenerationEvaluationCase CreateTwoClaimFixture()
    {
        var context = CreateContext();
        var item = Assert.Single(context.Items);
        var citation = new GenerationCitation(
            item.DocumentId,
            item.ContentSha256,
            item.Position.StartOffset,
            item.Position.Length);

        return new GenerationEvaluationCase(
            "answered-2",
            context,
            [
                new ExpectedGenerationClaim("A tutela provisoria foi deferida.", [citation]),
                new ExpectedGenerationClaim("A decisão consta do documento indicado.", [citation])
            ],
            false);
    }

    private static GenerationContext CreateContext()
    {
        const string excerpt = "A tutela provisoria foi deferida.";
        var position = SourcePosition.Create(0, excerpt.Length, excerpt.Length);
        var item = new GenerationContextItem(
            "case-1",
            "doc-1",
            "decisao.pdf",
            new string('a', 64),
            excerpt,
            position,
            1.0f);
        return new GenerationContext("case-1", "tutela", 1000, excerpt.Length, [item]);
    }
}
