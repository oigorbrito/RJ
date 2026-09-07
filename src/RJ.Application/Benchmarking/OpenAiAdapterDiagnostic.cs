namespace RJ.Application.Benchmarking;

public enum OpenAiAdapterFailureClass
{
    Authentication,
    ModelNotFound,
    RequestSchema,
    ResponseSchema,
    Network,
    Timeout,
    RateLimit,
    Other
}

public enum OpenAiAdapterStage
{
    Http,
    ResponseParse,
    StructuredOutputExtraction,
    JsonDeserialization,
    LocalValidation,
    ResultConstruction,
    Other
}

public sealed record OpenAiAdapterDiagnostic(
    OpenAiAdapterStage Stage,
    string InnerExceptionType,
    string? HttpStatus,
    string? ProviderErrorCode,
    string ProviderErrorMessage,
    string ExceptionMessage,
    string ModelSent,
    string Endpoint,
    OpenAiAdapterFailureClass FailureClass);

public sealed class OpenAiAdapterException : InvalidOperationException
{
    public OpenAiAdapterException(
        string message,
        Exception? innerException,
        OpenAiAdapterDiagnostic diagnostic)
        : base(message, innerException)
    {
        Diagnostic = diagnostic;
    }

    public OpenAiAdapterDiagnostic Diagnostic { get; }
}
