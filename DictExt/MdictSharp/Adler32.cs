namespace MdictSharp;

internal static class Adler32
{
    private const uint ModAdler = 65521;

    public static uint Compute(ReadOnlySpan<byte> data)
    {
        uint a = 1;
        uint b = 0;

        foreach (byte value in data)
        {
            a = (a + value) % ModAdler;
            b = (b + a) % ModAdler;
        }

        return (b << 16) | a;
    }
}
