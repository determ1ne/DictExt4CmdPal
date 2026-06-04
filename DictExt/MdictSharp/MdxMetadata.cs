namespace MdictSharp;

public sealed record MdxMetadata(
    string Title,
    string Description,
    string EncodingName,
    string Format,
    long WordCount,
    bool IsRightToLeft);

public sealed record MdxHeadword(string Text, long RecordOffset, long RecordSize);
