using System.Buffers.Binary;
using System.IO.Compression;

namespace MdictSharp;

internal static class MdictCompression
{
    public static byte[] Decompress(ReadOnlySpan<byte> block, long expectedDecompressedSize)
    {
        if (block.Length <= 8)
        {
            throw new MdictException("Compressed block is too small.");
        }

        uint type = BinaryPrimitives.ReadUInt32BigEndian(block);
        uint checksum = BinaryPrimitives.ReadUInt32BigEndian(block[4..]);
        ReadOnlySpan<byte> payload = block[8..];
        byte[] result;

        switch (type)
        {
            case 0x00000000:
                result = payload.ToArray();
                break;
            case 0x01000000:
                throw new NotSupportedException("LZO-compressed MDict blocks are not supported in this first version.");
            case 0x02000000:
                result = InflateZlib(payload, expectedDecompressedSize);
                break;
            default:
                throw new MdictException($"Unknown MDict compression type 0x{type:x8}.");
        }

        if (expectedDecompressedSize >= 0 && result.LongLength != expectedDecompressedSize)
        {
            throw new MdictException($"Decompressed block size mismatch. Expected {expectedDecompressedSize}, got {result.LongLength}.");
        }

        uint actual = Adler32.Compute(result);
        if (actual != checksum)
        {
            throw new MdictException($"Adler32 checksum mismatch. Expected 0x{checksum:x8}, got 0x{actual:x8}.");
        }

        return result;
    }

    private static byte[] InflateZlib(ReadOnlySpan<byte> payload, long expectedDecompressedSize)
    {
        using var compressed = new MemoryStream(payload.ToArray(), writable: false);
        using var zlib = new ZLibStream(compressed, CompressionMode.Decompress);
        using var decompressed = expectedDecompressedSize is > 0 and <= int.MaxValue
            ? new MemoryStream((int)expectedDecompressedSize)
            : new MemoryStream();
        zlib.CopyTo(decompressed);
        return decompressed.ToArray();
    }
}
