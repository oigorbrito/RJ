using RJ.Domain.Cases;

namespace RJ.Application.Sources;

public interface IProcessSourceAdapter
{
    string SourceSystem { get; }

    LegalCase Canonicalize(ProcessSourceDocument source);
}
