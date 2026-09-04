using RJ.Application.Generation;

namespace RJ.Application.Evaluation;

public sealed class GenerationEvaluator
{
    public GenerationEvaluationResult Evaluate(
        GenerationEvaluationCase evaluationCase,
        GenerationModelOutput output)
    {
        ArgumentNullException.ThrowIfNull(evaluationCase);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(evaluationCase.Context);
        ArgumentNullException.ThrowIfNull(evaluationCase.ExpectedClaims);
        ArgumentNullException.ThrowIfNull(output.Claims);

        var abstentionMatches = output.Abstained == evaluationCase.ExpectAbstention;
        if (evaluationCase.ExpectAbstention)
        {
            var passed = abstentionMatches
                && output.Claims.Count == 0
                && !string.IsNullOrWhiteSpace(output.AbstentionReason);

            return new GenerationEvaluationResult(
                evaluationCase.Id,
                output.Abstained,
                evaluationCase.ExpectedClaims.Count,
                output.Claims.Count,
                passed ? 1.0 : 0.0,
                passed ? 1.0 : 0.0,
                passed ? 1.0 : 0.0,
                passed);
        }

        if (!abstentionMatches || output.Abstained)
        {
            return new GenerationEvaluationResult(
                evaluationCase.Id,
                output.Abstained,
                evaluationCase.ExpectedClaims.Count,
                output.Claims.Count,
                0.0,
                0.0,
                0.0,
                false);
        }

        var validCitations = 0;
        var totalCitations = 0;
        var groundedClaims = 0;

        foreach (var claim in output.Claims)
        {
            if (claim is null || string.IsNullOrWhiteSpace(claim.Text) || claim.Citations is null)
            {
                continue;
            }

            var allClaimCitationsValid = claim.Citations.Count > 0;
            foreach (var citation in claim.Citations)
            {
                totalCitations++;
                if (citation is not null && CitationExistsInContext(evaluationCase.Context, citation))
                {
                    validCitations++;
                }
                else
                {
                    allClaimCitationsValid = false;
                }
            }

            if (allClaimCitationsValid && MatchesExpectedClaim(evaluationCase.ExpectedClaims, claim))
            {
                groundedClaims++;
            }
        }

        var matchedExpectedClaims = evaluationCase.ExpectedClaims.Count(expected =>
            output.Claims.Any(actual => actual is not null && MatchesExpectedClaim(expected, actual)));

        var claimRecall = evaluationCase.ExpectedClaims.Count == 0
            ? output.Claims.Count == 0 ? 1.0 : 0.0
            : (double)matchedExpectedClaims / evaluationCase.ExpectedClaims.Count;

        var citationValidity = totalCitations == 0 ? 0.0 : (double)validCitations / totalCitations;
        var groundedness = output.Claims.Count == 0 ? 0.0 : (double)groundedClaims / output.Claims.Count;

        var passed = claimRecall == 1.0
            && citationValidity == 1.0
            && groundedness == 1.0;

        return new GenerationEvaluationResult(
            evaluationCase.Id,
            false,
            evaluationCase.ExpectedClaims.Count,
            output.Claims.Count,
            claimRecall,
            citationValidity,
            groundedness,
            passed);
    }

    private static bool CitationExistsInContext(GenerationContext context, GenerationCitation citation) =>
        context.Items.Any(item =>
            StringComparer.Ordinal.Equals(item.DocumentId, citation.DocumentId)
            && StringComparer.Ordinal.Equals(item.ContentSha256, citation.ContentSha256)
            && item.Position.StartOffset == citation.StartOffset
            && item.Position.Length == citation.Length);

    private static bool MatchesExpectedClaim(
        IReadOnlyList<ExpectedGenerationClaim> expectedClaims,
        GenerationClaim actual) =>
        expectedClaims.Any(expected => MatchesExpectedClaim(expected, actual));

    private static bool MatchesExpectedClaim(
        ExpectedGenerationClaim expected,
        GenerationClaim actual)
    {
        if (!StringComparer.Ordinal.Equals(expected.Text, actual.Text)
            || expected.Citations.Count != actual.Citations.Count)
        {
            return false;
        }

        return expected.Citations.All(expectedCitation =>
            actual.Citations.Any(actualCitation => CitationEquals(expectedCitation, actualCitation)));
    }

    private static bool CitationEquals(GenerationCitation left, GenerationCitation right) =>
        StringComparer.Ordinal.Equals(left.DocumentId, right.DocumentId)
        && StringComparer.Ordinal.Equals(left.ContentSha256, right.ContentSha256)
        && left.StartOffset == right.StartOffset
        && left.Length == right.Length;
}
