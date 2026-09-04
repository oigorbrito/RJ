namespace RJ.Application.Operations;

public interface IReadinessProbe
{
    Task CheckAsync(CancellationToken cancellationToken);
}
