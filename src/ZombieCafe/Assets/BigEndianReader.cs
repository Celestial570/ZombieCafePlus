using System.Buffers.Binary;
using System.Text;

namespace ZombieCafe.Assets;

/// <summary>
/// The original DataInputStream: integers are big-endian, floats/doubles are raw little-endian
/// (the game memcpy'd them), strings are a u16 big-endian byte length followed by the bytes.
/// Reads past the end return zeros, as the original stream returned -1 bytes that were masked.
/// </summary>
public sealed class BigEndianReader
{
    readonly byte[] data;
    public int Position { get; set; }
    public int Length => data.Length;
    public bool AtEnd => Position >= data.Length;

    public BigEndianReader(byte[] data, int position = 0)
    {
        this.data = data;
        Position = position;
    }

    ReadOnlySpan<byte> Take(int n)
    {
        if (Position + n <= data.Length)
        {
            var s = data.AsSpan(Position, n);
            Position += n;
            return s;
        }
        var tmp = new byte[n];
        if (Position < data.Length)
            data.AsSpan(Position).CopyTo(tmp);
        Position += n;
        return tmp;
    }

    public bool ReadBoolean() => ReadByte() != 0;
    public byte ReadByte() => Take(1)[0];
    public sbyte ReadSByte() => (sbyte)Take(1)[0];
    public short ReadShort() => BinaryPrimitives.ReadInt16BigEndian(Take(2));
    public ushort ReadUShort() => BinaryPrimitives.ReadUInt16BigEndian(Take(2));
    public int ReadInt() => BinaryPrimitives.ReadInt32BigEndian(Take(4));
    public long ReadLong() => BinaryPrimitives.ReadInt64BigEndian(Take(8));
    public float ReadFloat() => BinaryPrimitives.ReadSingleLittleEndian(Take(4));
    public double ReadDouble() => BinaryPrimitives.ReadDoubleLittleEndian(Take(8));

    public string ReadUtf()
    {
        int len = ReadShort();
        if (len <= 0)
            return "";
        return Encoding.UTF8.GetString(Take(len));
    }

    public void Skip(int n) => Position += n;
}
