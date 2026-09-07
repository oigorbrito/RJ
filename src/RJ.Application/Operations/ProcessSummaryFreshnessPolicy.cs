using RJ.Application.Generation;
using RJ.Application.Security;

namespace RJ.Application.Operations;

public static class ProcessSummaryFreshnessPolicy
{
    public static ProcessSummaryFreshnessDecision Evaluate(
        ProcessSummaryJob job,
        string currentSnapshotSha256,
        string currentSummaryVersion,
        DateTimeOffset now,
        ProcessRetentionPolicy retention)
    {
        ArgumentNullException.ThrowIfNull(job);

        if (!StringComparer.Ordinal.Equals(job.SnapshotSha256, Require(currentSnapshotSha256, nameof(currentSnapshotSha256))))
        {
            return new ProcessSummaryFreshnessDecision(ProcessSummaryFreshnessStatus.Stale, "Snapshot hash changed.");
        }

        if (!StringComparer.Ordinal.Equals(job.SummaryVersion, Require(currentSummaryVersion, nameof(currentSummaryVersion))))
        {
            return new ProcessSummaryFreshnessDecision(ProcessSummaryFreshnessStatus.Stale, "Summary version changed.");
        }

        if (now - job.ValidatedAt > retention.SummaryTtl)
        {
            return new ProcessSummaryFreshnessDecision(ProcessSummaryFreshnessStatus.Expired, "Summary retention window expired.");
        }

        return new ProcessSummaryFreshnessDecision(ProcessSummaryFreshnessStatus.Fresh, null);
    }

    private static string Require(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Freshness value cannot be empty.", parameterName);
        }

        return value.Trim();
    }
}

public sealed record ProcessSummaryFreshnessDecision(
    ProcessSummaryFreshnessStatus Status,
    string? Reason);

public enum ProcessSummaryFreshnessStatus
{
    Fresh,
    Stale,
    Expired
}
