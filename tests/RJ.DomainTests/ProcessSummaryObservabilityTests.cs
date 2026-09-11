using System.Diagnostics.Metrics;
using System.Diagnostics;
using RJ.Application.Operations;

namespace RJ.DomainTests;

public sealed class ProcessSummaryObservabilityTests
{
    [Fact]
    public void Activity_meter_telemetry_emits_sanitized_job_and_duration_metrics()
    {
        var jobMeasurements = new List<(long Value, KeyValuePair<string, object?>[] Tags)>();
        var durationMeasurements = new List<(double Value, KeyValuePair<string, object?>[] Tags)>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Meter.Name == ActivityMeterProcessSummaryTelemetry.MeterName)
            {
                meterListener.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((instrument, measurement, tags, _) =>
        {
            if (instrument.Name == "process_summary_jobs_total")
            {
                jobMeasurements.Add((measurement, tags.ToArray()));
            }
        });
        listener.SetMeasurementEventCallback<double>((instrument, measurement, tags, _) =>
        {
            if (instrument.Name == "process_summary_duration_ms")
            {
                durationMeasurements.Add((measurement, tags.ToArray()));
            }
        });
        listener.Start();

        var telemetry = new ActivityMeterProcessSummaryTelemetry();
        telemetry.Record(new ProcessSummaryTelemetryEvent(
            "process_summary.validated",
            "job-1",
            "case-1",
            "6003160-36.2026.8.16.0021",
            new string('a', 64),
            "summary-v1",
            ProcessSummaryJobTelemetryStatus.Validated,
            DateTimeOffset.UtcNow,
            new Dictionary<string, string>
            {
                ["duration_ms"] = "125.5",
                ["status"] = "Validated",
                ["raw_content"] = "must-not-be-exported",
                ["claim_text"] = "must-not-be-exported"
            }));
        listener.RecordObservableInstruments();

        var jobs = Assert.Single(jobMeasurements);
        Assert.Equal(1, jobs.Value);
        Assert.Equal("Validated", Assert.Single(jobs.Tags, tag => tag.Key == "status").Value);

        var duration = Assert.Single(durationMeasurements);
        Assert.Equal(125.5, duration.Value);
        Assert.Equal("Validated", Assert.Single(duration.Tags, tag => tag.Key == "status").Value);
        Assert.DoesNotContain(jobMeasurements.SelectMany(item => item.Tags), tag =>
            tag.Key.Contains("raw", StringComparison.OrdinalIgnoreCase)
            || tag.Key.Contains("claim", StringComparison.OrdinalIgnoreCase)
            || tag.Key.Contains("content", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(durationMeasurements.SelectMany(item => item.Tags), tag =>
            tag.Key.Contains("raw", StringComparison.OrdinalIgnoreCase)
            || tag.Key.Contains("claim", StringComparison.OrdinalIgnoreCase)
            || tag.Key.Contains("content", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Activity_source_emits_correlation_tags_without_sensitive_attributes()
    {
        Activity? captured = null;
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == ActivityMeterProcessSummaryTelemetry.ActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStarted = activity => captured = activity
        };
        ActivitySource.AddActivityListener(listener);

        var telemetry = new ActivityMeterProcessSummaryTelemetry();
        telemetry.Record(new ProcessSummaryTelemetryEvent(
            "process_summary.validated",
            "job-2",
            "case-2",
            null,
            null,
            "summary-v1",
            ProcessSummaryJobTelemetryStatus.Validated,
            DateTimeOffset.UtcNow,
            new Dictionary<string, string>
            {
                ["status"] = "Validated",
                ["raw_content"] = "must-not-be-exported",
                ["claim_text"] = "must-not-be-exported"
            }));

        Assert.NotNull(captured);
        Assert.Equal("job-2", captured!.GetTagItem("job_id"));
        Assert.Equal("case-2", captured.GetTagItem("case_id"));
        Assert.DoesNotContain(captured.Tags, tag =>
            tag.Key.Contains("raw", StringComparison.OrdinalIgnoreCase)
            || tag.Key.Contains("claim", StringComparison.OrdinalIgnoreCase)
            || tag.Key.Contains("content", StringComparison.OrdinalIgnoreCase));
    }
}
