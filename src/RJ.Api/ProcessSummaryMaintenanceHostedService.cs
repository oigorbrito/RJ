using System.Globalization;
using RJ.Application.Operations;

#pragma warning disable CA1848, CA1873

namespace RJ.Api;

public sealed record ProcessSummaryMaintenanceOptions(
    bool Enabled,
    TimeSpan Interval,
    int BatchSize)
{
    public const string IntervalSecondsKey = "RJ_PROCESS_SUMMARY_MAINTENANCE_INTERVAL_SECONDS";
    public const string BatchSizeKey = "RJ_PROCESS_SUMMARY_MAINTENANCE_BATCH_SIZE";

    public static ProcessSummaryMaintenanceOptions FromConfiguration(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var intervalRaw = configuration[IntervalSecondsKey];
        var batchRaw = configuration[BatchSizeKey];
        var intervalMissing = string.IsNullOrWhiteSpace(intervalRaw);
        var batchMissing = string.IsNullOrWhiteSpace(batchRaw);

        if (intervalMissing && batchMissing)
        {
            return new ProcessSummaryMaintenanceOptions(false, TimeSpan.Zero, 0);
        }

        if (intervalMissing || batchMissing)
        {
            throw new InvalidOperationException(
                $"{IntervalSecondsKey} and {BatchSizeKey} must either both be configured or both be absent.");
        }

        if (!double.TryParse(intervalRaw, NumberStyles.Float, CultureInfo.InvariantCulture, out var intervalSeconds)
            || intervalSeconds <= 0
            || double.IsNaN(intervalSeconds)
            || double.IsInfinity(intervalSeconds)
            || intervalSeconds > TimeSpan.MaxValue.TotalSeconds)
        {
            throw new InvalidOperationException($"{IntervalSecondsKey} must be a positive finite interval representable by TimeSpan.");
        }

        if (!int.TryParse(batchRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var batchSize)
            || batchSize <= 0)
        {
            throw new InvalidOperationException($"{BatchSizeKey} must be a positive integer.");
        }

        return new ProcessSummaryMaintenanceOptions(
            true,
            TimeSpan.FromSeconds(intervalSeconds),
            batchSize);
    }
}

public sealed class ProcessSummaryMaintenanceHostedService(
    IConfiguration configuration,
    ProcessSummaryMaintenanceService maintenance,
    ILogger<ProcessSummaryMaintenanceHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var options = ProcessSummaryMaintenanceOptions.FromConfiguration(configuration);
        if (!options.Enabled)
        {
            logger.Log(LogLevel.Information, "Process-summary maintenance scheduler is disabled because no cadence is configured.");
            return;
        }

        logger.Log(
            LogLevel.Information,
            "Process-summary maintenance scheduler enabled with explicit interval {Interval} and batch size {BatchSize}.",
            options.Interval,
            options.BatchSize);

        while (!stoppingToken.IsCancellationRequested)
        {
            var result = await maintenance.RunOnceAsync(options.BatchSize, stoppingToken);
            logger.Log(
                LogLevel.Information,
                "Process-summary maintenance run evaluated {EvaluatedCount} jobs and dispatched {DispatchedCount} work items.",
                result.EvaluatedCount,
                result.DispatchedCount);

            await Task.Delay(options.Interval, stoppingToken);
        }
    }
}

public sealed class LoggingProcessSummaryRefreshDispatcher(
    ILogger<LoggingProcessSummaryRefreshDispatcher> logger) : IProcessSummaryRefreshDispatcher
{
    public Task DispatchAsync(
        ProcessSummaryMaintenanceWorkItem workItem,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(workItem);
        cancellationToken.ThrowIfCancellationRequested();
        logger.LogInformation(
            "Process-summary maintenance work item {WorkId} requires {Action} for job {JobId} case {CaseId}; reason={Reason}.",
            workItem.WorkId,
            workItem.Action,
            workItem.JobId,
            workItem.CaseId,
            workItem.Reason);
        return Task.CompletedTask;
    }
}

#pragma warning restore CA1848, CA1873
