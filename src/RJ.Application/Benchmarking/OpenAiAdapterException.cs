namespace RJ.Application.Benchmarking;

public sealed class OpenAiAdapterException : Exception
{
    public OpenAiAdapterException(
        string stage,
        string message,
        Exception? innerException = null,
        int? httpStatus = null,
        string? providerErrorCode = null,
        string? providerErrorMessage = null,
        string? responseObjectType = null,
        int? outputItemCount = null,
        IReadOnlyList<string>? contentItemTypes = null,
        bool? hasOutputText = null,
        bool? hasStructuredJson = null)
        : base(message, innerException)
    {
        if (string.IsNullOrWhiteSpace(stage))
        {
            throw new ArgumentException("Adapter failure stage is required.", nameof(stage));
        }

        Stage = stage;
        HttpStatus = httpStatus;
        ProviderErrorCode = providerErrorCode;
        ProviderErrorMessage = providerErrorMessage;
        ResponseObjectType = responseObjectType;
        OutputItemCount = outputItemCount;
        ContentItemTypes = contentItemTypes ?? Array.Empty<string>();
        HasOutputText = hasOutputText;
        HasStructuredJson = hasStructuredJson;
    }

    public string Stage { get; }
    public int? HttpStatus { get; }
    public string? ProviderErrorCode { get; }
    public string? ProviderErrorMessage { get; }
    public string? ResponseObjectType { get; }
    public int? OutputItemCount { get; }
    public IReadOnlyList<string> ContentItemTypes { get; }
    public bool? HasOutputText { get; }
    public bool? HasStructuredJson { get; }
}
