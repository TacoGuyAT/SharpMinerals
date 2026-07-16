using System.IO.Compression;
using System.Text;
using SharpMinerals.Network.Buffers;

namespace SharpMinerals.Network.Nbt;

/// <summary>
/// Minimal NBT parser (read counterpart to <see cref="NbtTag.WritePayload"/>), used to recover custom
/// item data the client echoes back, and to read NBT files (schematics). Parses into the in-memory
/// <see cref="NbtTag"/> model.
/// </summary>
public static class NbtReader {
    /// <summary>Reads a named root compound from an UNCOMPRESSED NBT stream (the payload form on-disk NBT
    /// files share with the wire). The root's name comes out in <paramref name="rootName"/> - often empty.</summary>
    public static NbtCompound ReadRoot(Stream stream, out string rootName) {
        var s = new MinecraftStream(stream);
        byte type = s.ReadUByte();
        if (type != (byte)NbtTagType.Compound)
            throw new FormatException($"NBT root must be a compound (10), got tag {type}.");
        rootName = ReadName(s);
        return ReadCompound(s);
    }

    /// <summary>Reads a gzip-compressed named root compound - the standard on-disk NBT file form
    /// (.schem, .dat). All reads go through ReadExactly, so gzip's partial reads are safe.</summary>
    public static NbtCompound ReadGzipped(Stream stream, out string rootName) {
        using var gz = new GZipStream(stream, CompressionMode.Decompress);
        return ReadRoot(gz, out rootName);
    }
    /// <summary>
    /// Reads an item Slot's NBT (positioned after the slot's id + count). A lone <see cref="NbtTagType.End"/>
    /// byte means no NBT (returns null); otherwise parses the 1.20.1 named root compound.
    /// </summary>
    public static NbtCompound? ReadItemNbt(MinecraftStream s) {
        byte type = s.ReadUByte();
        if (type == (byte)NbtTagType.End) return null;
        if (type != (byte)NbtTagType.Compound)
            throw new FormatException($"Item NBT root must be a compound (10), got tag {type}.");
        ReadName(s); // root name - empty in 1.20.1's named form
        return ReadCompound(s);
    }

    static NbtCompound ReadCompound(MinecraftStream s) {
        var compound = new NbtCompound();
        while (true) {
            byte type = s.ReadUByte();
            if (type == (byte)NbtTagType.End) break;
            string name = ReadName(s);
            compound.Children[name] = ReadPayload(s, (NbtTagType)type);
        }
        return compound;
    }

    static NbtTag ReadPayload(MinecraftStream s, NbtTagType type) => type switch {
        NbtTagType.Byte => new NbtByte(s.ReadByte2()),
        NbtTagType.Short => new NbtShort(s.ReadShort()),
        NbtTagType.Int => new NbtInt(s.ReadInt()),
        NbtTagType.Long => new NbtLong(s.ReadLong()),
        NbtTagType.Float => new NbtFloat(s.ReadFloat()),
        NbtTagType.Double => new NbtDouble(s.ReadDouble()),
        NbtTagType.ByteArray => new NbtByteArray(s.ReadBytes(s.ReadInt())),
        NbtTagType.String => new NbtString(ReadName(s)),
        NbtTagType.List => ReadList(s),
        NbtTagType.Compound => ReadCompound(s),
        NbtTagType.IntArray => ReadIntArray(s),
        NbtTagType.LongArray => ReadLongArray(s),
        _ => throw new FormatException($"Unknown NBT tag type {(byte)type}."),
    };

    static NbtList ReadList(MinecraftStream s) {
        var elementType = (NbtTagType)s.ReadUByte();
        int count = s.ReadInt();
        var list = new NbtList(elementType);
        if (elementType == NbtTagType.End) return list; // empty list - no element payloads follow
        for (int i = 0; i < count; i++)
            list.Add(ReadPayload(s, elementType));
        return list;
    }

    static NbtIntArray ReadIntArray(MinecraftStream s) {
        int n = s.ReadInt();
        var values = new int[n];
        for (int i = 0; i < n; i++) values[i] = s.ReadInt();
        return new NbtIntArray(values);
    }

    static NbtLongArray ReadLongArray(MinecraftStream s) {
        int n = s.ReadInt();
        var values = new long[n];
        for (int i = 0; i < n; i++) values[i] = s.ReadLong();
        return new NbtLongArray(values);
    }

    static string ReadName(MinecraftStream s) {
        int length = s.ReadUShort();
        return length == 0 ? string.Empty : Encoding.UTF8.GetString(s.ReadBytes(length));
    }
}
