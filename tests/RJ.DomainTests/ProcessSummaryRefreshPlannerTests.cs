using RJ.Application.Operations;

namespace RJ.DomainTests;

public sealed class ProcessSummaryRefreshPlannerTests
{
    [Fact]
    public void Plan_does_not_schedule_work_for_fresh_summary()
    {
        var plan = ProcessSummaryRefreshPlanner.Plan(new ProcessSummaryFreshnessDecision(ProcessSummaryFreshnessStatus.Fresh, null));

        Assert.Equal(ProcessSummaryRefreshAction.None, plan.Action);
        Assert.False(plan.RequiresScheduler);
        Assert.Equal("Summary is fresh.", plan.Reason);
    }

    [Fact]
    public void Plan_requests_refresh_for_stale_summary()
    {
        var plan = ProcessSummaryRefreshPlanner.Plan(new ProcessSummaryFreshnessDecision(ProcessSummaryFreshnessStatus.Stale, "Snapshot hash changed."));

        Assert.Equal(ProcessSummaryRefreshAction.Refresh, plan.Action);
        Assert.True(plan.RequiresScheduler);
        Assert.Equal("Snapshot hash changed.", plan.Reason);
    }

    [Fact]
    public void Plan_requests_rebuild_for_expired_summary()
    {
        var plan = ProcessSummaryRefreshPlanner.Plan(new ProcessSummaryFreshnessDecision(ProcessSummaryFreshnessStatus.Expired, "Summary retention window expired."));

        Assert.Equal(ProcessSummaryRefreshAction.Rebuild, plan.Action);
        Assert.True(plan.RequiresScheduler);
        Assert.Equal("Summary retention window expired.", plan.Reason);
    }
}
