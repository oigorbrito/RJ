using RJ.Domain.Cases;

namespace RJ.Application.Security;

public static class ProcessSecurityPolicy
{
    public static ProcessSecurityDecision AuthorizeProcessSummary(CallerContext caller, LegalCase legalCase)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(legalCase);

        if (!caller.IsAuthorizedFor(legalCase.Id))
        {
            return ProcessSecurityDecision.Deny("Caller is not authorized for this legal case.");
        }

        if (IsSealed(legalCase) && !caller.CanAccessSealedCases)
        {
            return ProcessSecurityDecision.Deny("Caller is not authorized for sealed legal cases.");
        }

        if (legalCase.Provenance.Any(item => !caller.IsAuthorizedForEvidenceSource(item.SourceName)))
        {
            return ProcessSecurityDecision.Deny("Caller is not authorized for one or more process evidence sources.");
        }

        return ProcessSecurityDecision.Allow();
    }

    public static ProcessRetentionPolicy DefaultRetentionPolicy() =>
        new("rjudi-process-summary-v1", TimeSpan.FromDays(90), TimeSpan.FromDays(30));

    private static bool IsSealed(LegalCase legalCase) => legalCase.SecrecyLevel > 0;
}

public sealed record ProcessSecurityDecision(bool IsAllowed, string? DenialReason)
{
    public static ProcessSecurityDecision Allow() => new(true, null);

    public static ProcessSecurityDecision Deny(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("Denial reason cannot be empty.", nameof(reason));
        }

        return new ProcessSecurityDecision(false, reason.Trim());
    }
}

public sealed record ProcessRetentionPolicy(
    string PolicyId,
    TimeSpan SummaryTtl,
    TimeSpan EvidenceTtl)
{
    public string PolicyId { get; } = string.IsNullOrWhiteSpace(PolicyId)
        ? throw new ArgumentException("Retention policy identifier cannot be empty.", nameof(PolicyId))
        : PolicyId.Trim();
}
