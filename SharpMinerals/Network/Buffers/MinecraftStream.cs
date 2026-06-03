using System.Buffers.Binary;
using System.Text;

namespace SharpMinerals.Network.Buffers;

/// <summary>
/// A <see cref="Stream"/> decorator that reads/writes Java Edition protocol data types (VarInt,
/// length-prefixed strings, big-endian numerics, UUIDs). The single funnel for message wire bytes.
/// </summary>
public sealed class MinecraftStream : Stream {
    public const int VarIntMaxBytes = 5;
    public const int VarLongMaxBytes = 10;

    Stream inner; // not readonly: EnableEncryption wraps it in an AES/CFB8 stream mid-connection
    readonly bool leaveOpen;
    int pushback = -1; // a single peeked byte, re-served on the next read (used to sniff a new connection's framing)

    /// <summary>The type mapper for the current encode (set by <see cref="Protocol.EncodePayload"/>); null outside encoding.</summary>
    public TypeMapper? Types { get; set; }

    public MinecraftStream(Stream inner, bool leaveOpen = false) {
        this.inner = inner;
        this.leaveOpen = leaveOpen;
    }

    public MinecraftStream() : this(new MemoryStream(), false) { }

    // Stream plumbing delegated to the wrapped stream.
    public override bool CanRead => inner.CanRead;
    public override bool CanSeek => inner.CanSeek;
    public override bool CanWrite => inner.CanWrite;
    public override long Length => inner.Length;
    public override long Position { get => inner.Position; set => inner.Position = value; }
    public override void Flush() => inner.Flush();
    public override int Read(byte[] buffer, int offset, int count) {
        // Serve a peeked byte first so a sniffed connection reads identically to an un-sniffed one.
        if (pushback >= 0 && count > 0) {
            buffer[offset] = (byte)pushback;
            pushback = -1;
            if (count == 1) return 1;
            int more = inner.Read(buffer, offset + 1, count - 1);
            return 1 + (more < 0 ? 0 : more);
        }
        return inner.Read(buffer, offset, count);
    }
    public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);
    public override void SetLength(long value) => inner.SetLength(value);
    public override void Write(byte[] buffer, int offset, int count) => inner.Write(buffer, offset, count);

    protected override void Dispose(bool disposing) {
        if (disposing && !leaveOpen)
            inner.Dispose();
        base.Dispose(disposing);
    }

    /// <summary>
    /// Switches the underlying stream to AES/CFB8 encryption (legacy 1.5.2 login); shared secret is key and IV.
    /// Must be called with no byte peeked (the plaintext handshake is fully consumed by now).
    /// </summary>
    public void EnableEncryption(byte[] sharedSecret) {
        if (pushback >= 0) throw new InvalidOperationException("Cannot enable encryption with a peeked byte pending.");
        inner = new AesCfb8Stream(inner, sharedSecret);
    }

    /// <summary>Materialises everything written so far (only valid over a MemoryStream).</summary>
    public byte[] ToArray() => inner is MemoryStream ms
        ? ms.ToArray()
        : throw new InvalidOperationException("ToArray() requires a MemoryStream-backed MinecraftStream.");

    // ── Primitive reads ─────────────────────────────────────────────────────
    public byte ReadUByte() {
        if (pushback >= 0) { byte p = (byte)pushback; pushback = -1; return p; }
        int b = inner.ReadByte();
        if (b < 0) throw new EndOfStreamException();
        return (byte)b;
    }

    /// <summary>
    /// Reads one byte but leaves it to be returned again by the next read — used to sniff a new
    /// connection's framing (legacy pre-Netty clients open with a raw packet id, not a VarInt frame).
    /// </summary>
    public byte PeekUByte() {
        byte b = ReadUByte();
        pushback = b;
        return b;
    }

    public sbyte ReadByte2() => (sbyte)ReadUByte();
    public bool ReadBool() => ReadUByte() != 0;

    public byte[] ReadBytes(int count) {
        var buf = new byte[count];
        ReadExactly(buf);
        return buf;
    }

    /// <summary>Reads everything left in the (length-bounded) buffer — e.g. a Custom Payload's data.</summary>
    public byte[] ReadRemaining() => ReadBytes((int)(Length - Position));

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

    /// <summary>
    /// Reads a legacy (pre-1.7) string: a big-endian <c>short</c> count of UTF-16 code units
    /// followed by that many UTF-16BE chars. Used by the JE61 (1.5.2) protocol.
    /// </summary>
    public string ReadString16(int maxLength = 32767) {
        int length = ReadUShort();
        if (length > maxLength)
            throw new FormatException($"Legacy string length {length} is out of range.");
        return Encoding.BigEndianUnicode.GetString(ReadBytes(length * 2));
    }

    /// <summary>Reads a legacy short-length-prefixed byte array (used by the 1.5.2 encryption packets).</summary>
    public byte[] ReadByteArray16() {
        int length = ReadShort();
        if (length < 0) throw new FormatException($"Byte-array length {length} is negative.");
        return ReadBytes(length);
    }

    /// <summary>Advances past a 1.5.2 Slot to keep a length-prefix-free legacy packet in sync.</summary>
    public void SkipLegacySlot() => ReadLegacySlot();

    /// <summary>Reads a 1.5.2 Slot: item id (-1 = empty), count, damage; trailing NBT consumed and discarded.</summary>
    public (short Id, byte Count, short Damage) ReadLegacySlot() {
        short id = ReadShort();
        if (id == -1) return (-1, 0, 0);
        byte count = ReadUByte();
        short damage = ReadShort();
        short nbtLength = ReadShort();
        if (nbtLength >= 0) ReadBytes(nbtLength); // gzip NBT
        return (id, count, damage);
    }

    public Guid ReadUuid() {
        // Two big-endian longs (msb first), as MC encodes UUIDs.
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

    /// <summary>
    /// Writes a legacy (pre-1.7) string: a big-endian <c>short</c> count of UTF-16 code units
    /// followed by the UTF-16BE chars. Used by the JE61 (1.5.2) protocol.
    /// </summary>
    public void WriteString16(string value) {
        WriteShort((short)value.Length);
        inner.Write(Encoding.BigEndianUnicode.GetBytes(value));
    }

    /// <summary>Writes a legacy short-length-prefixed byte array (used by the 1.5.2 encryption packets).</summary>
    public void WriteByteArray16(byte[] data) {
        WriteShort((short)data.Length);
        inner.Write(data, 0, data.Length);
    }

    public void WriteUuid(Guid value) {
        (long msb, long lsb) = MsbLsbFromUuid(value);
        Span<byte> b = stackalloc byte[16];
        BinaryPrimitives.WriteInt64BigEndian(b[..8], msb);
        BinaryPrimitives.WriteInt64BigEndian(b[8..], lsb);
        inner.Write(b);
    }

    // ── Angle: a rotation in degrees packed into one byte (1/256 of a turn) ──
    public void WriteAngle(float degrees) => WriteUByte((byte)(int)MathF.Floor(degrees * 256f / 360f));
    public float ReadAngle() => ReadUByte() * 360f / 256f;

    // ── Slot (an item stack on the wire) ────────────────────────────────────
    // present(bool); if present: VarInt item id, byte count, NBT (0x00 = none).
    public void WriteEmptySlot() => WriteBool(false);

    public void WriteSlot(int itemId, int count) {
        WriteBool(true);
        WriteVarInt(itemId);
        WriteByte2((sbyte)count);
        WriteUByte(0x00); // empty NBT (TAG_End)
    }

    /// <summary>Writes a 1.5.2 Slot: short item id (-1 = empty → stop); else byte count, short damage, and
    /// short NBT length -1 (no gzip NBT). Mirrors <see cref="ReadLegacySlot"/>.</summary>
    public void WriteLegacySlot(short itemId, byte count, short damage) {
        WriteShort(itemId);
        if (itemId < 0) return; // empty slot — nothing follows
        WriteUByte(count);
        WriteShort(damage);
        WriteShort(-1); // no NBT
    }

    /// <summary>Reads a Slot's presence + item id + count, leaving trailing NBT unread (discarded with the per-packet buffer).</summary>
    public (int ItemId, int Count)? ReadSlotLite() {
        if (!ReadBool()) return null;
        int id = ReadVarInt();
        int count = ReadByte2();
        return (id, count);
    }

    // ── Position (block coordinate packed into a long) ──────────────────────
    // Layout: x (26 bits) | z (26 bits) | y (12 bits), most-significant first.
    // https://minecraft.wiki/w/Java_Edition_protocol#Position
    public void WritePosition(long x, long y, long z) =>
        WriteLong(((x & 0x3FFFFFF) << 38) | ((z & 0x3FFFFFF) << 12) | (y & 0xFFF));

    public (long X, long Y, long Z) ReadPosition() {
        long value = ReadLong();
        // Arithmetic shifts sign-extend each packed field back to a signed value.
        long x = value >> 38;
        long y = value << 52 >> 52;
        long z = value << 26 >> 38;
        return (x, y, z);
    }

    // ── VarInt size helper (needed for packet framing) ──────────────────────
    public static int VarIntSize(int value) {
        uint v = (uint)value;
        int size = 1;
        while ((v & ~0x7Fu) != 0) { v >>= 7; size++; }
        return size;
    }

    /// <summary>Builds a Guid from 16 big-endian bytes (Java's UUID byte order); used for offline-mode UUIDs.</summary>
    public static Guid GuidFromBigEndianBytes(ReadOnlySpan<byte> b) {
        if (b.Length != 16) throw new ArgumentException("UUID requires 16 bytes.", nameof(b));
        long msb = BinaryPrimitives.ReadInt64BigEndian(b[..8]);
        long lsb = BinaryPrimitives.ReadInt64BigEndian(b[8..]);
        return UuidFromMsbLsb(msb, lsb);
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
