using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace RJ.Application.Operations;

public interface IProcessSummaryTelemetry
{
    void Record(ProcessSummaryTelemetryEvent telemetryEvent);
}

public interface IProcessSummaryStructuredLogger
{
    void Record(ProcessSummaryLogEvent logEvent);
}

public sealed class NoopProcessSummaryStructuredLogger : IProcessSummaryStructuredLogger
{
    public void Record(ProcessSummaryLogEvent logEvent) => ArgumentNullException.ThrowIfNull(logEvent);
}

public sealed class InMemoryProcessSummaryStructuredLogger : IProcessSummaryStructuredLogger
{
    private readonly List<ProcessSummaryLogEvent> events = [];
    private readonly object sync = new();

    public IReadOnlyList<ProcessSummaryLogEvent> Events
    {
        get
        {
            lock (sync)
            {
                return events.ToArray();
            }
        }
    }

    public void Record(ProcessSummaryLogEvent logEvent)
    {
        ArgumentNullException.ThrowIfNull(logEvent);
        lock (sync)
        {
            events.Add(logEvent);
        }
    }
}

public sealed record ProcessSummaryLogEvent(
    string EventName,
    string Outcome,
    string? JobId,
    string? CaseId,
    string TenantIdHash,
    string SubjectIdHash,
    DateTimeOffset ObservedAt)
{
    public string EventName { get; } = Required(EventName, nameof(EventName));
    public string Outcome { get; } = Required(Outcome, nameof(Outcome));
    public string? JobId { get; } = JobId;
    public string? CaseId { get; } = CaseId;
    public string TenantIdHash { get; } = Required(TenantIdHash, nameof(TenantIdHash));
    public string SubjectIdHash { get; } = Required(SubjectIdHash, nameof(SubjectIdHash));
    public DateTimeOffset ObservedAt { get; } = ObservedAt == default
        ? throw new ArgumentException("Log observed instant cannot be empty.", nameof(ObservedAt))
        : ObservedAt;

    private static string Required(string value, string parameterName) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("Log value cannot be empty.", parameterName)
            : value.Trim();
}

public sealed class NoopProcessSummaryTelemetry : IProcessSummaryTelemetry
{
    public void Record(ProcessSummaryTelemetryEvent telemetryEvent)
    {
        ArgumentNullException.ThrowIfNull(telemetryEvent);
    }
}

public sealed class ActivityMeterProcessSummaryTelemetry : IProcessSummaryTelemetry
{
    public const string ActivitySourceName = "RJ.ProcessSummary";
    public const string MeterName = "RJ.ProcessSummary";

    private static readonly ActivitySource ActivitySource = new(ActivitySourceName);
    private static readonly Meter Meter = new(MeterName);
    private static readonly Counter<long> Jobs = Meter.CreateCounter<long>("process_summary_jobs_total");
    private static readonly Histogram<double> Duration = Meter.CreateHistogram<double>("process_summary_duration_ms");

    public void Record(ProcessSummaryTelemetryEvent telemetryEvent)
    {
        ArgumentNullException.ThrowIfNull(telemetryEvent);

        var status = telemetryEvent.Status.ToString();
        Jobs.Add(1, new KeyValuePair<string, object?>("status", status));
        if (telemetryEvent.Tags.TryGetValue("duration_ms", out var duration)
            && double.TryParse(duration, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var durationMs))
        {
            Duration.Record(durationMs, new KeyValuePair<string, object?>("status", status));
        }

        using var activity = ActivitySource.StartActivity(telemetryEvent.EventName);
        if (activity is null)
        {
            return;
        }

        AddTag(activity, "job_id", telemetryEvent.JobId);
        AddTag(activity, "case_id", telemetryEvent.CaseId);
        AddTag(activity, "summary_version", telemetryEvent.SummaryVersion);
        foreach (var tag in telemetryEvent.Tags)
        {
            if (tag.Key.Contains("content", StringComparison.OrdinalIgnoreCase)
                || tag.Key.Contains("raw", StringComparison.OrdinalIgnoreCase)
                || tag.Key.Contains("claim", StringComparison.OrdinalIgnoreCase)
                || tag.Key.Contains("attachment", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            activity.SetTag(tag.Key, tag.Value);
        }
    }

    private static void AddTag(Activity activity, string name, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            activity.SetTag(name, value);
        }
    }
}

public interface IProcessSummaryAuditSink
{
    void Record(ProcessSummaryAuditEvent auditEvent);
}

public sealed class NoopProcessSummaryAuditSink : IProcessSummaryAuditSink
{
    public void Record(ProcessSummaryAuditEvent auditEvent) => ArgumentNullException.ThrowIfNull(auditEvent);
}

public sealed class InMemoryProcessSummaryAuditSink : IProcessSummaryAuditSink
{
    private readonly List<ProcessSummaryAuditEvent> events = [];
    private readonly object sync = new();

    public IReadOnlyList<ProcessSummaryAuditEvent> Events
    {
        get
        {
            lock (sync)
            {
                return events.ToArray();
            }
        }
    }

    public void Record(ProcessSummaryAuditEvent auditEvent)
    {
        ArgumentNullException.ThrowIfNull(auditEvent);
        lock (sync)
        {
            events.Add(auditEvent);
        }
    }
}

public sealed record ProcessSummaryAuditEvent(
    string Action,
    string Outcome,
    string? JobId,
    string? CaseId,
    string SubjectIdHash,
    DateTimeOffset ObservedAt,
    string ReasonCode)
{
    public string Action { get; } = Required(Action, nameof(Action));
    public string Outcome { get; } = Required(Outcome, nameof(Outcome));
    public string? JobId { get; } = JobId;
    public string? CaseId { get; } = CaseId;
    public string SubjectIdHash { get; } = Required(SubjectIdHash, nameof(SubjectIdHash));
    public DateTimeOffset ObservedAt { get; } = ObservedAt == default
        ? throw new ArgumentException("Audit observed instant cannot be empty.", nameof(ObservedAt))
        : ObservedAt;
    public string ReasonCode { get; } = Required(ReasonCode, nameof(ReasonCode));

    private static string Required(string value, string parameterName) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("Audit value cannot be empty.", parameterName)
            : value.Trim();
}

public sealed class InMemoryProcessSummaryTelemetry : IProcessSummaryTelemetry
{
    private readonly List<ProcessSummaryTelemetryEvent> events = [];
    private readonly object sync = new();

    public IReadOnlyList<ProcessSummaryTelemetryEvent> Events
    {
        get
        {
            lock (sync)
            {
                return events.ToArray();
            }
        }
    }

    public void Record(ProcessSummaryTelemetryEvent telemetryEvent)
    {
        ArgumentNullException.ThrowIfNull(telemetryEvent);
        lock (sync)
        {
            events.Add(telemetryEvent);
        }
    }
}

public sealed record ProcessSummaryTelemetryEvent(
    string EventName,
    string? JobId,
    string? CaseId,
    string? Cnj,
    string? SnapshotSha256,
    string? SummaryVersion,
    ProcessSummaryJobTelemetryStatus Status,
    DateTimeOffset ObservedAt,
    IReadOnlyDictionary<string, string> Tags)
{
    public string EventName { get; } = Require(EventName, nameof(EventName));

    public IReadOnlyDictionary<string, string> Tags { get; } =
        (Tags ?? throw new ArgumentNullException(nameof(Tags)))
        .ToDictionary(
            item => Require(item.Key, nameof(Tags)),
            item => Require(item.Value, nameof(Tags)),
            StringComparer.Ordinal);

    private static string Require(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Telemetry value cannot be empty.", parameterName);
        }

        return value.Trim();
    }
}

public enum ProcessSummaryJobTelemetryStatus
{
    Submitted,
    IdempotentReplay,
    IdempotencyConflict,
    Forbidden,
    Validated,
    Failed,
    Freshness,
    RefreshPlan
}
