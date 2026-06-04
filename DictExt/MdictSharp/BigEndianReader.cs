using System.Buffers.Binary;
using System.Text;

namespace MdictSharp;

internal sealed partial class BigEndianReader : IDisposable
{
    private readonly Stream _stream;
    private readonly bool _leaveOpen;
    private readonly byte[] _buffer = new byte[8];

    public BigEndianReader(Stream stream, bool leaveOpen = false)
    {
        _stream = stream;
        _leaveOpen = leaveOpen;
    }

    public long Position
    {
        get => _stream.Position;
        set => _stream.Position = value;
    }

    public int ReadInt32()
    {
        ReadExactly(_buffer.AsSpan(0, 4));
        return BinaryPrimitives.ReadInt32BigEndian(_buffer.AsSpan(0, 4));
    }

    public uint ReadUInt32()
    {
        ReadExactly(_buffer.AsSpan(0, 4));
        return BinaryPrimitives.ReadUInt32BigEndian(_buffer.AsSpan(0, 4));
    }

    public ushort ReadUInt16()
    {
        ReadExactly(_buffer.AsSpan(0, 2));
        return BinaryPrimitives.ReadUInt16BigEndian(_buffer.AsSpan(0, 2));
    }

    public byte ReadByte()
    {
        int value = _stream.ReadByte();
        if (value < 0)
        {
            throw new EndOfStreamException();
        }

        return (byte)value;
    }

    public long ReadNumber(int size)
    {
        return size switch
        {
            4 => ReadUInt32(),
            8 => ReadInt64(),
            _ => throw new ArgumentOutOfRangeException(nameof(size), "MDict numbers are 4 or 8 bytes.")
        };
    }

    public long ReadInt64()
    {
        ReadExactly(_buffer);
        return BinaryPrimitives.ReadInt64BigEndian(_buffer);
    }

    public byte[] ReadBytes(int count)
    {
        byte[] bytes = new byte[count];
        ReadExactly(bytes);
        return bytes;
    }

    public string ReadNullTerminatedString(Encoding encoding)
    {
        using var bytes = new MemoryStream();
        if (encoding.CodePage == Encoding.Unicode.CodePage)
        {
            while (true)
            {
                byte lo = ReadByte();
                byte hi = ReadByte();
                if (lo == 0 && hi == 0)
                {
                    break;
                }

                bytes.WriteByte(lo);
                bytes.WriteByte(hi);
            }
        }
        else
        {
            while (true)
            {
                byte b = ReadByte();
                if (b == 0)
                {
                    break;
                }

                bytes.WriteByte(b);
            }
        }

        return encoding.GetString(bytes.ToArray());
    }

    private void ReadExactly(Span<byte> destination)
    {
        _stream.ReadExactly(destination);
    }

    public void Dispose()
    {
        if (!_leaveOpen)
        {
            _stream.Dispose();
        }
    }
}
