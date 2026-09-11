namespace RJ.Application.Operations;

public static class ProcessSummaryObservabilityCatalog
{
    public const string CatalogVersion = "rjudi-process-summary-observability-v2";

    public static ProcessSummaryObservabilityDefinition Current() =>
        new(
            CatalogVersion,
            [
                new ProcessSummaryMetricDefinition(
                    "process_summary_jobs_total",
                    "counter",
                    "Counts submitted, validated, failed, forbidden, idempotent replay, and idempotency-conflict summary job events.",
                    ["status"]),
                new ProcessSummaryMetricDefinition(
                    "process_summary_validation_failures_total",
                    "counter",
                    "Counts process summary validator failures without recording claim text, raw source, or attachment content.",
                    ["reason"]),
                new ProcessSummaryMetricDefinition(
                    "process_summary_duration_ms",
                    "histogram",
                    "Measures end-to-end deterministic summary job duration in milliseconds.",
                    ["status"]),
                new ProcessSummaryMetricDefinition(
                    "process_summary_staleness_total",
                    "counter",
                    "Counts freshness decisions for snapshot, summary-version, and retention checks.",
                    ["decision"]),
                new ProcessSummaryMetricDefinition(
                    "process_summary_refresh_plans_total",
                    "counter",
                    "Counts deterministic refresh planner decisions.",
                    ["decision"]),
                new ProcessSummaryMetricDefinition(
                    "process_summary_maintenance_dispatches_total",
                    "counter",
                    "Counts deterministic maintenance work items dispatched for stale or expired summaries.",
                    ["action"]),
                new ProcessSummaryMetricDefinition(
                    "process_summary_retention_blocks_total",
                    "counter",
                    "Counts validated-summary publication attempts blocked by the active summary retention policy.",
                    ["decision"])
            ],
            [
                new ProcessSummaryTraceDefinition(
                    "process_summary.submit",
                    ["tenant_id_hash", "subject_id_hash", "case_id", "cnj", "snapshot_sha256", "summary_version", "retrieval_calls"]),
                new ProcessSummaryTraceDefinition(
                    "process_summary.submitted",
                    ["tenant_id_hash", "subject_id_hash", "case_id", "cnj", "snapshot_sha256", "summary_version", "retrieval_calls"]),
                new ProcessSummaryTraceDefinition(
                    "process_summary.idempotent_replay",
                    ["tenant_id_hash", "subject_id_hash", "job_id", "case_id", "cnj", "snapshot_sha256", "summary_version"]),
                new ProcessSummaryTraceDefinition(
                    "process_summary.idempotency_conflict",
                    ["tenant_id_hash", "subject_id_hash", "snapshot_sha256"]),
                new ProcessSummaryTraceDefinition(
                    "process_summary.forbidden",
                    ["tenant_id_hash", "subject_id_hash", "case_id", "cnj", "snapshot_sha256", "summary_version"]),
                new ProcessSummaryTraceDefinition(
                    "process_summary.validate",
                    [
                        "case_id",
                        "cnj",
                        "summary_version",
                        "validator_status",
                        "attempts",
                        "duration_ms",
                        "retried",
                        "validation_error_count",
                        "validation_error_reason"
                    ]),
                new ProcessSummaryTraceDefinition(
                    "process_summary.validated",
                    ["tenant_id_hash", "subject_id_hash", "job_id", "case_id", "cnj", "snapshot_sha256", "summary_version", "validator_status", "attempts", "duration_ms", "retried"]),
                new ProcessSummaryTraceDefinition(
                    "process_summary.failed",
                    ["tenant_id_hash", "subject_id_hash", "job_id", "case_id", "cnj", "snapshot_sha256", "summary_version", "validator_status", "attempts", "duration_ms", "retried", "validation_error_count", "validation_error_reason"]),
                new ProcessSummaryTraceDefinition(
                    "process_summary.freshness",
                    ["job_id", "case_id", "cnj", "snapshot_sha256", "summary_version", "decision"]),
                new ProcessSummaryTraceDefinition(
                    "process_summary.refresh_plan",
                    ["job_id", "case_id", "cnj", "snapshot_sha256", "summary_version", "decision", "action", "reason", "requires_scheduler"]),
                new ProcessSummaryTraceDefinition(
                    "process_summary.maintenance_dispatch",
                    ["job_id", "case_id", "cnj", "snapshot_sha256", "summary_version", "action", "freshness", "work_id"]),
                new ProcessSummaryTraceDefinition(
                    "process_summary.retention_blocked_publication",
                    ["job_id", "case_id", "cnj", "snapshot_sha256", "summary_version", "decision", "publication", "policy_id"])
            ],
            [
                new ProcessSummarySloDefinition(
                    "deterministic_summary_validation_success",
                    "All admitted deterministic process summary jobs must either validate or expose explicit validation failures.",
                    "100% of completed deterministic jobs have terminal status Validated or Failed.",
                    "Local contract only; not a production availability SLA."),
                new ProcessSummarySloDefinition(
                    "no_sensitive_observability_payload",
                    "Telemetry, metrics, and traces must not include raw process source, raw CPF/CNPJ, prompt body, claim text, or attachment text.",
                    "100% of emitted observability fields pass sensitive-token exclusion tests.",
                    "Local contract only; production exporters are not selected.")
            ],
            [
                "raw_content",
                "raw_cpf_cnpj",
                "prompt_body",
                "claim_text",
                "attachment_text"
            ]);
}

public sealed record ProcessSummaryObservabilityDefinition(
    string Version,
    IReadOnlyList<ProcessSummaryMetricDefinition> Metrics,
    IReadOnlyList<ProcessSummaryTraceDefinition> Traces,
    IReadOnlyList<ProcessSummarySloDefinition> Slos,
    IReadOnlyList<string> ForbiddenPayloads);

public sealed record ProcessSummaryMetricDefinition(
    string Name,
    string Kind,
    string Description,
    IReadOnlyList<string> Labels);

public sealed record ProcessSummaryTraceDefinition(
    string Name,
    IReadOnlyList<string> Attributes);

public sealed record ProcessSummarySloDefinition(
    string Name,
    string Objective,
    string Target,
    string Scope);
