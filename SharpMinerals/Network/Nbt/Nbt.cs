using System.Buffers.Binary;
using System.Text;
using SharpMinerals.Network.Buffers;

namespace SharpMinerals.Network.Nbt;

/// <summary>
/// Named Binary Tag types as defined by https://minecraft.wiki/w/NBT_format.
/// </summary>
public enum NbtTagType : byte {
    End = 0,
    Byte = 1,
    Short = 2,
    Int = 3,
    Long = 4,
    Float = 5,
    Double = 6,
    ByteArray = 7,
    String = 8,
    List = 9,
    Compound = 10,
    IntArray = 11,
    LongArray = 12,
}

public abstract class NbtTag {
    public abstract NbtTagType Type { get; }

    /// <summary>Writes just this tag's payload (no type id, no name).</summary>
    public abstract void WritePayload(NbtStream stream);

    /// <summary>
    /// Writes a complete root tag: type id, optional name, payload. Set <paramref name="network"/> for
    /// the 1.20.2+ nameless form; 1.20.1 (763) still expects the empty name.
    /// </summary>
    public void WriteRoot(MinecraftStream stream, bool network = false) {
        var nbt = new NbtStream(stream);
        nbt.WriteByte((byte)Type);
        if (!network) nbt.WriteName(string.Empty);
        WritePayload(nbt);
    }

    public byte[] ToBytes(bool network = false) {
        using var ms = new MemoryStream();
        WriteRoot(new MinecraftStream(ms, leaveOpen: true), network);
        return ms.ToArray();
    }
}

public sealed class NbtByte : NbtTag {
    public sbyte Value;
    public NbtByte(sbyte value) => Value = value;
    public override NbtTagType Type => NbtTagType.Byte;
    public override void WritePayload(NbtStream s) => s.WriteByte((byte)Value);
}

public sealed class NbtShort : NbtTag {
    public short Value;
    public NbtShort(short value) => Value = value;
    public override NbtTagType Type => NbtTagType.Short;
    public override void WritePayload(NbtStream s) => s.WriteShort(Value);
}

public sealed class NbtInt : NbtTag {
    public int Value;
    public NbtInt(int value) => Value = value;
    public override NbtTagType Type => NbtTagType.Int;
    public override void WritePayload(NbtStream s) => s.WriteInt(Value);
}

public sealed class NbtLong : NbtTag {
    public long Value;
    public NbtLong(long value) => Value = value;
    public override NbtTagType Type => NbtTagType.Long;
    public override void WritePayload(NbtStream s) => s.WriteLong(Value);
}

public sealed class NbtFloat : NbtTag {
    public float Value;
    public NbtFloat(float value) => Value = value;
    public override NbtTagType Type => NbtTagType.Float;
    public override void WritePayload(NbtStream s) => s.WriteFloat(Value);
}

public sealed class NbtDouble : NbtTag {
    public double Value;
    public NbtDouble(double value) => Value = value;
    public override NbtTagType Type => NbtTagType.Double;
    public override void WritePayload(NbtStream s) => s.WriteDouble(Value);
}

public sealed class NbtString : NbtTag {
    public string Value;
    public NbtString(string value) => Value = value;
    public override NbtTagType Type => NbtTagType.String;
    public override void WritePayload(NbtStream s) => s.WriteName(Value);
}

public sealed class NbtByteArray : NbtTag {
    public byte[] Value;
    public NbtByteArray(byte[] value) => Value = value;
    public override NbtTagType Type => NbtTagType.ByteArray;
    public override void WritePayload(NbtStream s) {
        s.WriteInt(Value.Length);
        s.WriteRaw(Value);
    }
}

public sealed class NbtIntArray : NbtTag {
    public int[] Value;
    public NbtIntArray(int[] value) => Value = value;
    public override NbtTagType Type => NbtTagType.IntArray;
    public override void WritePayload(NbtStream s) {
        s.WriteInt(Value.Length);
        foreach (var v in Value) s.WriteInt(v);
    }
}

public sealed class NbtLongArray : NbtTag {
    public long[] Value;
    public NbtLongArray(long[] value) => Value = value;
    public override NbtTagType Type => NbtTagType.LongArray;
    public override void WritePayload(NbtStream s) {
        s.WriteInt(Value.Length);
        foreach (var v in Value) s.WriteLong(v);
    }
}

public sealed class NbtList : NbtTag {
    public NbtTagType ElementType;
    public readonly List<NbtTag> Items = new();
    public NbtList(NbtTagType elementType) => ElementType = elementType;
    public override NbtTagType Type => NbtTagType.List;

    public NbtList Add(NbtTag tag) {
        if (tag.Type != ElementType)
            throw new InvalidOperationException($"List of {ElementType} cannot hold a {tag.Type}.");
        Items.Add(tag);
        return this;
    }

    public override void WritePayload(NbtStream s) {
        s.WriteByte((byte)(Items.Count == 0 ? NbtTagType.End : ElementType));
        s.WriteInt(Items.Count);
        foreach (var t in Items) t.WritePayload(s);
    }
}

public sealed class NbtCompound : NbtTag {
    public readonly Dictionary<string, NbtTag> Children = new();
    public override NbtTagType Type => NbtTagType.Compound;

    public NbtTag this[string name] {
        get => Children[name];
        set => Children[name] = value;
    }

    public NbtCompound Put(string name, NbtTag tag) { Children[name] = tag; return this; }
    public NbtCompound Put(string name, sbyte value) => Put(name, new NbtByte(value));
    public NbtCompound Put(string name, short value) => Put(name, new NbtShort(value));
    public NbtCompound Put(string name, int value) => Put(name, new NbtInt(value));
    public NbtCompound Put(string name, long value) => Put(name, new NbtLong(value));
    public NbtCompound Put(string name, float value) => Put(name, new NbtFloat(value));
    public NbtCompound Put(string name, double value) => Put(name, new NbtDouble(value));
    public NbtCompound Put(string name, string value) => Put(name, new NbtString(value));
    public NbtCompound Put(string name, bool value) => Put(name, new NbtByte((sbyte)(value ? 1 : 0)));

    public override void WritePayload(NbtStream s) {
        foreach (var (name, tag) in Children) {
            s.WriteByte((byte)tag.Type);
            s.WriteName(name);
            tag.WritePayload(s);
        }
        s.WriteByte((byte)NbtTagType.End);
    }
}

/// <summary>
/// Big-endian byte sink for NBT payloads. NBT uses 2-byte-length-prefixed UTF-8 names/strings,
/// unlike the VarInt-prefixed strings elsewhere, hence its own wrapper over <see cref="MinecraftStream"/>.
/// </summary>
public sealed class NbtStream {
    readonly MinecraftStream stream;
    public NbtStream(MinecraftStream stream) => this.stream = stream;

    public void WriteByte(byte value) => stream.WriteUByte(value);
    public void WriteShort(short value) => stream.WriteShort(value);
    public void WriteInt(int value) => stream.WriteInt(value);
    public void WriteLong(long value) => stream.WriteLong(value);
    public void WriteFloat(float value) => stream.WriteFloat(value);
    public void WriteDouble(double value) => stream.WriteDouble(value);
    public void WriteRaw(byte[] value) => stream.Write(value, 0, value.Length);

    public void WriteName(string value) {
        var bytes = Encoding.UTF8.GetBytes(value);
        if (bytes.Length > ushort.MaxValue)
            throw new FormatException("NBT string exceeds 65535 bytes.");
        Span<byte> len = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(len, (ushort)bytes.Length);
        stream.Write(len.ToArray(), 0, 2);
        stream.Write(bytes, 0, bytes.Length);
    }
}
