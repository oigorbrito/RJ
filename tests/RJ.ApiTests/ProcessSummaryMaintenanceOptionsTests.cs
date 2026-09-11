using Microsoft.Extensions.Configuration;
using RJ.Api;

namespace RJ.ApiTests;

public sealed class ProcessSummaryMaintenanceOptionsTests
{
    [Fact]
    public void Scheduler_is_disabled_when_both_settings_are_absent()
    {
        var configuration = new ConfigurationBuilder().Build();

        var options = ProcessSummaryMaintenanceOptions.FromConfiguration(configuration);

        Assert.False(options.Enabled);
        Assert.Equal(TimeSpan.Zero, options.Interval);
        Assert.Equal(0, options.BatchSize);
    }

    [Fact]
    public void Scheduler_uses_only_explicit_interval_and_batch_size()
    {
        var configuration = Configuration(new Dictionary<string, string?>
        {
            [ProcessSummaryMaintenanceOptions.IntervalSecondsKey] = "17.5",
            [ProcessSummaryMaintenanceOptions.BatchSizeKey] = "23"
        });

        var options = ProcessSummaryMaintenanceOptions.FromConfiguration(configuration);

        Assert.True(options.Enabled);
        Assert.Equal(TimeSpan.FromSeconds(17.5), options.Interval);
        Assert.Equal(23, options.BatchSize);
    }

    [Theory]
    [InlineData("10", null)]
    [InlineData(null, "10")]
    [InlineData("0", "10")]
    [InlineData("NaN", "10")]
    [InlineData("10", "0")]
    [InlineData("10", "not-an-integer")]
    public void Partial_or_invalid_scheduler_configuration_fails_closed(
        string? interval,
        string? batchSize)
    {
        var configuration = Configuration(new Dictionary<string, string?>
        {
            [ProcessSummaryMaintenanceOptions.IntervalSecondsKey] = interval,
            [ProcessSummaryMaintenanceOptions.BatchSizeKey] = batchSize
        });

        Assert.Throws<InvalidOperationException>(
            () => ProcessSummaryMaintenanceOptions.FromConfiguration(configuration));
    }

    private static IConfiguration Configuration(Dictionary<string, string?> values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
}
