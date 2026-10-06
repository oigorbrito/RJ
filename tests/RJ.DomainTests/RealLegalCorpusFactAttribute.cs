namespace RJ.DomainTests;

// Enabling this lane requires externally supplied corpora; missing/invalid paths then fail.
public sealed class RealLegalCorpusFactAttribute : FactAttribute
{
    public RealLegalCorpusFactAttribute()
    {
        if (!StringComparer.Ordinal.Equals(Environment.GetEnvironmentVariable("RJ_RUN_REAL_CORPUS_TESTS"), "1"))
        {
            Skip = "Real legal corpus NOT_EXECUTED: set RJ_RUN_REAL_CORPUS_TESTS=1 and supply both corpus roots.";
        }
    }
}
