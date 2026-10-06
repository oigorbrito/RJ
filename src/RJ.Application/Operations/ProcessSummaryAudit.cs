namespace RJ.Application.Operations;

public interface IProcessSummaryAuditSink
{
    void Record(ProcessSummaryAuditEvent auditEvent);
}

public sealed class NoopProcessSummaryAuditSink : IProcessSummaryAuditSink
{
    public void Record(ProcessSummaryAuditEvent auditEvent)
    {
        ArgumentNullException.ThrowIfNull(auditEvent);
    }
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
    string EventName,
    string Decision,
    string? JobId,
    string? CaseId,
    string? TenantIdHash,
    string? SubjectIdHash,
    DateTimeOffset ObservedAt)
{
    public string EventName { get; } = Require(EventName, nameof(EventName));

    public string Decision { get; } = Require(Decision, nameof(Decision));

    private static string Require(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Audit value cannot be empty.", parameterName);
        }

        return value.Trim();
    }
}
