namespace RJ.Application.Security;

public interface IProcessSummaryJobAccessStore
{
    Task<bool> TryBindAsync(
        string jobId,
        CallerContext caller,
        string caseId,
        CancellationToken cancellationToken);

    Task<bool> CanAccessAsync(
        string jobId,
        CallerContext caller,
        CancellationToken cancellationToken);
}
