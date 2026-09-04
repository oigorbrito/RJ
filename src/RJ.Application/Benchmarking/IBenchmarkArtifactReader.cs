namespace RJ.Application.Benchmarking;

public interface IBenchmarkArtifactReader
{
    Task<ReadOnlyMemory<byte>> ReadAsync(
        string artifactReference,
        CancellationToken cancellationToken);
}
