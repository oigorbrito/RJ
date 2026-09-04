namespace RJ.Application.Generation;

public interface IGenerationModel
{
    Task<GenerationModelOutput> GenerateAsync(
        GenerationContext context,
        CancellationToken cancellationToken);
}
