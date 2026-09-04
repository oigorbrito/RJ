namespace RJ.Application.Benchmarking;

public sealed record GenerationBenchmarkMetadata(
    string GitCommit,
    string Runtime,
    string CatalogVersion,
    string ModelId,
    string ModelConfiguration,
    string Seed)
{
    public void Validate()
    {
        ValidateRequired(GitCommit, nameof(GitCommit));
        ValidateRequired(Runtime, nameof(Runtime));
        ValidateRequired(CatalogVersion, nameof(CatalogVersion));
        ValidateRequired(ModelId, nameof(ModelId));
        ValidateRequired(ModelConfiguration, nameof(ModelConfiguration));
        ValidateRequired(Seed, nameof(Seed));
    }

    private static void ValidateRequired(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"{parameterName} cannot be empty.", parameterName);
        }
    }
}
