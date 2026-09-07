using RJ.Domain.Cases;

namespace RJ.Application.Sources;

public sealed class ProcessSourceCanonicalizationService
{
    private readonly Dictionary<string, IProcessSourceAdapter> adapters;

    public ProcessSourceCanonicalizationService(IEnumerable<IProcessSourceAdapter> adapters)
    {
        ArgumentNullException.ThrowIfNull(adapters);
        this.adapters = adapters.ToDictionary(
            adapter => ProcessSourceDocument.RequireSourceSystem(adapter.SourceSystem),
            StringComparer.OrdinalIgnoreCase);
    }

    public LegalCase Canonicalize(ProcessSourceDocument source)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (!adapters.TryGetValue(source.SourceSystem, out var adapter))
        {
            throw new InvalidOperationException($"No process source adapter is registered for '{source.SourceSystem}'.");
        }

        return adapter.Canonicalize(source);
    }
}
