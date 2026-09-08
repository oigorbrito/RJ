using RJ.Application.Operations;

namespace RJ.DomainTests;

public sealed class ProcessSummaryObservabilityCatalogTests
{
    private static readonly string[] RequiredMetrics =
    [
        "process_summary_jobs_total",
        "process_summary_validation_failures_total",
        "process_summary_duration_ms",
        "process_summary_staleness_total",
        "process_summary_refresh_plans_total"
    ];

    private static readonly string[] ForbiddenPayloads =
    [
        "raw_content",
        "raw_cpf_cnpj",
        "prompt_body",
        "claim_text",
        "attachment_text"
    ];

    [Fact]
    public void Current_catalog_is_versioned_and_defines_process_summary_metrics_traces_and_slos()
    {
        var catalog = ProcessSummaryObservabilityCatalog.Current();

        Assert.Equal(ProcessSummaryObservabilityCatalog.CatalogVersion, catalog.Version);
        foreach (var metric in RequiredMetrics)
        {
            Assert.Contains(catalog.Metrics, item => item.Name == metric);
        }

        Assert.Contains(catalog.Metrics, item =>
            item.Name == "process_summary_duration_ms"
            && item.Description.Contains("end-to-end", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(catalog.Traces, item =>
            item.Name == "process_summary.submitted"
            && item.Attributes.Contains("retrieval_calls"));
        Assert.Contains(catalog.Traces, item =>
            item.Name == "process_summary.idempotent_replay"
            && item.Attributes.Contains("job_id"));
        Assert.Contains(catalog.Traces, item =>
            item.Name == "process_summary.idempotency_conflict"
            && item.Attributes.Contains("snapshot_sha256"));
        Assert.Contains(catalog.Traces, item =>
            item.Name == "process_summary.forbidden"
            && item.Attributes.Contains("tenant_id_hash"));
        Assert.Contains(catalog.Traces, item =>
            item.Name == "process_summary.submit"
            && item.Attributes.Contains("snapshot_sha256")
            && item.Attributes.Contains("retrieval_calls"));
        Assert.Contains(catalog.Traces, item =>
            item.Name == "process_summary.refresh_plan"
            && item.Attributes.Contains("job_id")
            && item.Attributes.Contains("snapshot_sha256")
            && item.Attributes.Contains("summary_version")
            && item.Attributes.Contains("decision")
            && item.Attributes.Contains("action")
            && item.Attributes.Contains("reason")
            && item.Attributes.Contains("requires_scheduler"));
        Assert.Contains(catalog.Traces, item =>
            item.Name == "process_summary.freshness"
            && item.Attributes.Contains("job_id")
            && item.Attributes.Contains("decision"));
        Assert.Contains(catalog.Traces, item =>
            item.Name == "process_summary.validate"
            && item.Attributes.Contains("attempts")
            && item.Attributes.Contains("duration_ms")
            && item.Attributes.Contains("retried")
            && item.Attributes.Contains("validation_error_count")
            && item.Attributes.Contains("validation_error_reason"));
        Assert.Contains(catalog.Traces, item =>
            item.Name == "process_summary.validated"
            && item.Attributes.Contains("validator_status"));
        Assert.Contains(catalog.Traces, item =>
            item.Name == "process_summary.failed"
            && item.Attributes.Contains("validation_error_reason"));
        Assert.Contains(catalog.Slos, item => item.Name == "no_sensitive_observability_payload");
        Assert.All(catalog.Slos, item => Assert.Contains("Local contract", item.Scope, StringComparison.Ordinal));
    }

    [Fact]
    public void Current_catalog_forbids_sensitive_payload_classes()
    {
        var catalog = ProcessSummaryObservabilityCatalog.Current();

        foreach (var payload in ForbiddenPayloads)
        {
            Assert.Contains(payload, catalog.ForbiddenPayloads);
        }

        Assert.DoesNotContain(catalog.Metrics.SelectMany(item => item.Labels), item => item.Contains("raw", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(catalog.Traces.SelectMany(item => item.Attributes), item => item.Contains("prompt_body", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(catalog.Traces.SelectMany(item => item.Attributes), item => item.Contains("attachment_text", StringComparison.OrdinalIgnoreCase));
    }
}
