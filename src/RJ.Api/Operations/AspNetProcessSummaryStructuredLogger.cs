using System.Diagnostics;
using Microsoft.Extensions.Logging;
using RJ.Application.Operations;

namespace RJ.Api.Operations;

public sealed partial class AspNetProcessSummaryStructuredLogger(ILogger<AspNetProcessSummaryStructuredLogger> logger)
    : IProcessSummaryStructuredLogger
{
    public void Record(ProcessSummaryLogEvent logEvent)
    {
        ArgumentNullException.ThrowIfNull(logEvent);
        LogLifecycle(
            logEvent.EventName,
            logEvent.Outcome,
            logEvent.JobId,
            logEvent.CaseId,
            logEvent.TenantIdHash,
            logEvent.SubjectIdHash,
            logEvent.ObservedAt,
            Activity.Current?.Id);
    }

    [LoggerMessage(
        EventId = 4201,
        Level = LogLevel.Information,
        Message = "Process summary lifecycle event {EventName} outcome={Outcome} job_id={JobId} case_id={CaseId} tenant_id_hash={TenantIdHash} subject_id_hash={SubjectIdHash} correlation_id={CorrelationId} observed_at={ObservedAt}")]
    private partial void LogLifecycle(
        string eventName,
        string outcome,
        string? jobId,
        string? caseId,
        string tenantIdHash,
        string subjectIdHash,
        DateTimeOffset observedAt,
        string? correlationId);
}
