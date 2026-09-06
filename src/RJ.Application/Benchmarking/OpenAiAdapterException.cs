namespace RJ.Application.Benchmarking;

public sealed class OpenAiAdapterException : Exception
{
    public OpenAiAdapterException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
