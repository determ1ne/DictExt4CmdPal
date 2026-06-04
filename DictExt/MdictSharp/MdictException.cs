namespace MdictSharp;

public sealed class MdictException : Exception
{
    public MdictException(string message) : base(message)
    {
    }

    public MdictException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
