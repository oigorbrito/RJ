namespace RJ.Api;

public static class IngestionLimits
{
    public const long MaxRequestBodyBytes = 10L * 1024 * 1024;
    public const int MaxRawContentBytes = 8 * 1024 * 1024;
}
