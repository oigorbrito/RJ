namespace RJ.Application.Retrieval;

public sealed record SourcePosition
{
    private SourcePosition(int startOffset, int length)
    {
        StartOffset = startOffset;
        Length = length;
    }

    public int StartOffset { get; }

    public int Length { get; }

    public int EndOffset => checked(StartOffset + Length);

    public static SourcePosition Create(int startOffset, int length, int sourceLength)
    {
        if (startOffset < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(startOffset));
        }

        if (length <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(length));
        }

        if (sourceLength < 0 || startOffset > sourceLength - length)
        {
            throw new ArgumentOutOfRangeException(nameof(length), "Source position must be fully contained in the source text.");
        }

        return new SourcePosition(startOffset, length);
    }
}
