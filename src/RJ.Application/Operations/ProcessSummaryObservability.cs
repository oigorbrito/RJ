namespace RJ.Application.Operations;

public interface IProcessSummaryTelemetry
{
    void Record(ProcessSummaryTelemetryEvent telemetryEvent);
}

public sealed class NoopProcessSummaryTelemetry : IProcessSummaryTelemetry
{
    public void Record(ProcessSummaryTelemetryEvent telemetryEvent)
    {
        ArgumentNullException.ThrowIfNull(telemetryEvent);
    }
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
    Failed
}
