using System.Buffers.Binary;
using System.Text;

namespace SharpMinerals.Network.Buffers;

/// <summary>
/// A <see cref="Stream"/> decorator that knows how to read and write the data
/// types defined by the Java Edition protocol (VarInt, length-prefixed strings,
/// big-endian numerics, UUIDs, ...). It is the single funnel through which every
/// message is turned into wire bytes and back, so codecs never touch raw sockets.
/// See https://minecraft.wiki/w/Java_Edition_protocol#Data_types.
/// </summary>
public sealed class MinecraftStream : Stream {
    public const int VarIntMaxBytes = 5;
    public const int VarLongMaxBytes = 10;

    readonly Stream inner;
    readonly bool leaveOpen;

    public MinecraftStream(Stream inner, bool leaveOpen = false) {
        this.inner = inner;
        this.leaveOpen = leaveOpen;
    }

    public MinecraftStream() : this(new MemoryStream(), false) { }

    // ── Stream plumbing (delegated to the wrapped stream) ───────────────────
    public override bool CanRead => inner.CanRead;
    public override bool CanSeek => inner.CanSeek;
    public override bool CanWrite => inner.CanWrite;
    public override long Length => inner.Length;
    public override long Position { get => inner.Position; set => inner.Position = value; }
    public override void Flush() => inner.Flush();
    public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
    public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);
    public override void SetLength(long value) => inner.SetLength(value);
    public override void Write(byte[] buffer, int offset, int count) => inner.Write(buffer, offset, count);

    protected override void Dispose(bool disposing) {
        if (disposing && !leaveOpen)
            inner.Dispose();
        base.Dispose(disposing);
    }

    /// <summary>Materialises everything written so far (only valid over a MemoryStream).</summary>
    public byte[] ToArray() => inner is MemoryStream ms
        ? ms.ToArray()
        : throw new InvalidOperationException("ToArray() requires a MemoryStream-backed MinecraftStream.");

    // ── Primitive reads ─────────────────────────────────────────────────────
    public byte ReadUByte() {
        int b = inner.ReadByte();
        if (b < 0) throw new EndOfStreamException();
        return (byte)b;
    }

    public sbyte ReadByte2() => (sbyte)ReadUByte();
    public bool ReadBool() => ReadUByte() != 0;

    public void ReadExactly(Span<byte> buffer) {
        int read = 0;
        while (read < buffer.Length) {
            int n = inner.Read(buffer[read..]);
            if (n <= 0) throw new EndOfStreamException();
            read += n;
        }
    }

    public byte[] ReadBytes(int count) {
        var buf = new byte[count];
        ReadExactly(buf);
        return buf;
    }

    public ushort ReadUShort() {
        Span<byte> b = stackalloc byte[2];
        ReadExactly(b);
        return BinaryPrimitives.ReadUInt16BigEndian(b);
    }

    public short ReadShort() => (short)ReadUShort();

    public int ReadInt() {
        Span<byte> b = stackalloc byte[4];
        ReadExactly(b);
        return BinaryPrimitives.ReadInt32BigEndian(b);
    }

    public long ReadLong() {
        Span<byte> b = stackalloc byte[8];
        ReadExactly(b);
        return BinaryPrimitives.ReadInt64BigEndian(b);
    }

    public float ReadFloat() => BitConverter.Int32BitsToSingle(ReadInt());
    public double ReadDouble() => BitConverter.Int64BitsToDouble(ReadLong());

    public int ReadVarInt() {
        int value = 0, shift = 0;
        byte b;
        do {
            if (shift >= VarIntMaxBytes * 7)
                throw new FormatException("VarInt is too big.");
            b = ReadUByte();
            value |= (b & 0x7F) << shift;
            shift += 7;
        } while ((b & 0x80) != 0);
        return value;
    }

    public long ReadVarLong() {
        long value = 0; int shift = 0;
        byte b;
        do {
            if (shift >= VarLongMaxBytes * 7)
                throw new FormatException("VarLong is too big.");
            b = ReadUByte();
            value |= (long)(b & 0x7F) << shift;
            shift += 7;
        } while ((b & 0x80) != 0);
        return value;
    }

    public string ReadString(int maxLength = 32767) {
        int length = ReadVarInt();
        if (length < 0 || length > maxLength * 4)
            throw new FormatException($"String length {length} is out of range.");
        var bytes = ReadBytes(length);
        return Encoding.UTF8.GetString(bytes);
    }

    public Guid ReadUuid() {
        // Two big-endian longs (most-significant first), as MC encodes UUIDs.
        Span<byte> b = stackalloc byte[16];
        ReadExactly(b);
        long msb = BinaryPrimitives.ReadInt64BigEndian(b[..8]);
        long lsb = BinaryPrimitives.ReadInt64BigEndian(b[8..]);
        return UuidFromMsbLsb(msb, lsb);
    }

    // ── Primitive writes ────────────────────────────────────────────────────
    public void WriteUByte(byte value) => inner.WriteByte(value);
    public void WriteByte2(sbyte value) => inner.WriteByte((byte)value);
    public void WriteBool(bool value) => inner.WriteByte(value ? (byte)1 : (byte)0);

    public void WriteUShort(ushort value) {
        Span<byte> b = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(b, value);
        inner.Write(b);
    }

    public void WriteShort(short value) => WriteUShort((ushort)value);

    public void WriteInt(int value) {
        Span<byte> b = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(b, value);
        inner.Write(b);
    }

    public void WriteLong(long value) {
        Span<byte> b = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(b, value);
        inner.Write(b);
    }

    public void WriteFloat(float value) => WriteInt(BitConverter.SingleToInt32Bits(value));
    public void WriteDouble(double value) => WriteLong(BitConverter.DoubleToInt64Bits(value));

    public void WriteVarInt(int value) {
        uint v = (uint)value;
        while (true) {
            if ((v & ~0x7Fu) == 0) {
                inner.WriteByte((byte)v);
                return;
            }
            inner.WriteByte((byte)((v & 0x7F) | 0x80));
            v >>= 7;
        }
    }

    public void WriteVarLong(long value) {
        ulong v = (ulong)value;
        while (true) {
            if ((v & ~0x7Ful) == 0) {
                inner.WriteByte((byte)v);
                return;
            }
            inner.WriteByte((byte)((v & 0x7F) | 0x80));
            v >>= 7;
        }
    }

    public void WriteString(string value) {
        var bytes = Encoding.UTF8.GetBytes(value);
        WriteVarInt(bytes.Length);
        inner.Write(bytes);
    }

    public void WriteUuid(Guid value) {
        (long msb, long lsb) = MsbLsbFromUuid(value);
        Span<byte> b = stackalloc byte[16];
        BinaryPrimitives.WriteInt64BigEndian(b[..8], msb);
        BinaryPrimitives.WriteInt64BigEndian(b[8..], lsb);
        inner.Write(b);
    }

    // ── VarInt size helper (needed for packet framing) ──────────────────────
    public static int VarIntSize(int value) {
        uint v = (uint)value;
        int size = 1;
        while ((v & ~0x7Fu) != 0) { v >>= 7; size++; }
        return size;
    }

    // ── UUID <-> (msb, lsb) using Java's big-endian byte ordering ───────────
    static Guid UuidFromMsbLsb(long msb, long lsb) {
        Span<byte> b = stackalloc byte[16];
        BinaryPrimitives.WriteInt64BigEndian(b[..8], msb);
        BinaryPrimitives.WriteInt64BigEndian(b[8..], lsb);
        // Build a Guid from big-endian bytes so round-tripping is stable.
        return new Guid(
            BinaryPrimitives.ReadInt32BigEndian(b[0..4]),
            BinaryPrimitives.ReadInt16BigEndian(b[4..6]),
            BinaryPrimitives.ReadInt16BigEndian(b[6..8]),
            b[8], b[9], b[10], b[11], b[12], b[13], b[14], b[15]);
    }

    static (long msb, long lsb) MsbLsbFromUuid(Guid value) {
        Span<byte> g = stackalloc byte[16];
        value.TryWriteBytes(g);
        Span<byte> b = stackalloc byte[16];
        // Reverse the mixed-endian layout .NET uses for the first three groups.
        BinaryPrimitives.WriteInt32BigEndian(b[0..4], BitConverter.ToInt32(g[0..4]));
        BinaryPrimitives.WriteInt16BigEndian(b[4..6], BitConverter.ToInt16(g[4..6]));
        BinaryPrimitives.WriteInt16BigEndian(b[6..8], BitConverter.ToInt16(g[6..8]));
        g[8..].CopyTo(b[8..]);
        return (BinaryPrimitives.ReadInt64BigEndian(b[..8]), BinaryPrimitives.ReadInt64BigEndian(b[8..]));
    }
}
