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

public static class ProcessSummaryJobAccessStoreCompatibility
{
    public static bool TryBind(
        this IProcessSummaryJobAccessStore store,
        string jobId,
        CallerContext caller,
        string caseId) =>
        store.TryBindAsync(jobId, caller, caseId, CancellationToken.None)
            .GetAwaiter()
            .GetResult();

    public static bool CanAccess(
        this IProcessSummaryJobAccessStore store,
        string jobId,
        CallerContext caller) =>
        store.CanAccessAsync(jobId, caller, CancellationToken.None)
            .GetAwaiter()
            .GetResult();
}
