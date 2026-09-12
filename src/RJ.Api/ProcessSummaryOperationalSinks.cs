using RJ.Application.Operations;

#pragma warning disable CA1848, CA1873

namespace RJ.Api;

public sealed class LoggingProcessSummaryAuditSink(ILogger<LoggingProcessSummaryAuditSink> logger)
    : IProcessSummaryAuditSink
{
    public void Record(ProcessSummaryAuditEvent auditEvent)
    {
        ArgumentNullException.ThrowIfNull(auditEvent);
        logger.Log(
            LogLevel.Information,
            "RJudi audit {EventName} {Decision} Job={JobId} Case={CaseId} TenantHash={TenantIdHash} SubjectHash={SubjectIdHash} ObservedAt={ObservedAt}",
            auditEvent.EventName,
            auditEvent.Decision,
            auditEvent.JobId,
            auditEvent.CaseId,
            auditEvent.TenantIdHash,
            auditEvent.SubjectIdHash,
            auditEvent.ObservedAt);
    }
}

public sealed class LoggingProcessSummaryTelemetry(ILogger<LoggingProcessSummaryTelemetry> logger)
    : IProcessSummaryTelemetry
{
    public void Record(ProcessSummaryTelemetryEvent telemetryEvent)
    {
        ArgumentNullException.ThrowIfNull(telemetryEvent);
        logger.Log(
            LogLevel.Information,
            "RJudi telemetry {EventName} {Status} Job={JobId} Case={CaseId} Cnj={Cnj} Snapshot={SnapshotSha256} SummaryVersion={SummaryVersion} ObservedAt={ObservedAt} Tags={Tags}",
            telemetryEvent.EventName,
            telemetryEvent.Status,
            telemetryEvent.JobId,
            telemetryEvent.CaseId,
            telemetryEvent.Cnj,
            telemetryEvent.SnapshotSha256,
            telemetryEvent.SummaryVersion,
            telemetryEvent.ObservedAt,
            telemetryEvent.Tags);
    }
}

#pragma warning restore CA1848, CA1873
