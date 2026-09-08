namespace RJ.Application.Operations;

public interface IProcessSummaryClock
{
    DateTimeOffset UtcNow { get; }
}

public sealed class SystemProcessSummaryClock : IProcessSummaryClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
