namespace RJ.Application.Operations;

public static class ProcessSummaryRefreshPlanner
{
    public static ProcessSummaryRefreshPlan Plan(ProcessSummaryFreshnessDecision freshness)
    {
        ArgumentNullException.ThrowIfNull(freshness);

        return freshness.Status switch
        {
            ProcessSummaryFreshnessStatus.Fresh => new ProcessSummaryRefreshPlan(
                ProcessSummaryRefreshAction.None,
                "Summary is fresh.",
                false),
            ProcessSummaryFreshnessStatus.Stale => new ProcessSummaryRefreshPlan(
                ProcessSummaryRefreshAction.Refresh,
                freshness.Reason ?? "Summary is stale.",
                true),
            ProcessSummaryFreshnessStatus.Expired => new ProcessSummaryRefreshPlan(
                ProcessSummaryRefreshAction.Rebuild,
                freshness.Reason ?? "Summary is expired.",
                true),
            _ => throw new ArgumentOutOfRangeException(nameof(freshness), freshness.Status, "Unsupported freshness status.")
        };
    }
}

public sealed record ProcessSummaryRefreshPlan(
    ProcessSummaryRefreshAction Action,
    string Reason,
    bool RequiresScheduler);

public enum ProcessSummaryRefreshAction
{
    None,
    Refresh,
    Rebuild
}
