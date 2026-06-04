namespace MdictSharp;

internal static class MdictEncryption
{
    private static readonly byte[] KeySalt = [0x95, 0x36, 0x00, 0x00];

    public static void DecryptHeadwordIndex(Span<byte> buffer)
    {
        if (buffer.Length < 8)
        {
            throw new MdictException("Encrypted headword index block is too small.");
        }

        byte[] hashInput = new byte[8];
        buffer.Slice(4, 4).CopyTo(hashInput);
        KeySalt.CopyTo(hashInput.AsSpan(4));
        byte[] key = Ripemd128.ComputeHash(hashInput);

        byte previous = 0x36;
        for (int i = 8; i < buffer.Length; i++)
        {
            byte original = buffer[i];
            byte value = (byte)((original >> 4) | (original << 4));
            value = (byte)(value ^ previous ^ ((i - 8) & 0xff) ^ key[(i - 8) % key.Length]);
            previous = original;
            buffer[i] = value;
        }
    }
}
