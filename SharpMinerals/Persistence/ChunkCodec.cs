using SharpMinerals.Blocks;
using SharpMinerals.Blocks.Descriptors;
using SharpMinerals.Components;
using SharpMinerals.Level;
using SharpMinerals.Math;
using SharpMinerals.Network.Buffers;

namespace SharpMinerals.Persistence;

/// <summary>Serializes a <see cref="Chunk"/> to a self-describing <c>byte[]</c>. Uses a chunk-local palette
/// header (distinct block NAMES written once, then each cell references a palette index — compact and
/// id-shift safe), followed by sparse per-cell states and block entities.</summary>
public static class ChunkCodec {
    const byte Version = 1;
    const Mint Volume = Chunk.Size * Chunk.Size * Chunk.Size;

    public static byte[] Serialize(Chunk chunk) {
        using var ms = new MemoryStream();
        var s = new MinecraftStream(ms);
        s.WriteUByte(Version);

        // Palette header: distinct block ids → names, in first-seen order.
        var raw = chunk.RawStates;
        var indexOf = new Dictionary<ushort, int>();
        var names = new List<string>();
        foreach (var id in raw)
            if (indexOf.TryAdd(id, names.Count))
                names.Add(BlockRegistry.FromState(id).Id.Full);
        s.WriteVarInt(names.Count);
        foreach (var name in names) s.WriteString(name);

        foreach (var id in raw) s.WriteVarInt(indexOf[id]); // dense cells as palette indices

        // Sparse per-cell block states (chest facing, wool colour, …).
        s.WriteVarInt(chunk.CellStates.Count);
        foreach (var (cell, state) in chunk.CellStates) {
            s.WriteVarInt(cell);
            if (state.Type.TryGet<StatesBlockDescriptor>(out var sp))
                foreach (var property in sp.States) s.WriteVarInt(state.Get(property));
        }

        // Block entities (e.g. a chest's contents).
        s.WriteVarInt(chunk.Entities.Count);
        foreach (var entity in chunk.Entities) WriteEntity(s, entity);

        return ms.ToArray();
    }

    public static Chunk Deserialize(Vector3i position, byte[] data) {
        using var ms = new MemoryStream(data, writable: false);
        var s = new MinecraftStream(ms);
        if (s.ReadUByte() is var version && version != Version)
            throw new NotSupportedException($"Unknown chunk format version {version}.");

        var chunk = new Chunk(position);
        var raw = chunk.RawStates;

        int paletteCount = s.ReadVarInt();
        var palette = new ushort[paletteCount];
        for (int i = 0; i < paletteCount; i++)
            palette[i] = (ushort)(BlockRegistry.FromName(s.ReadString())?.BlockId ?? 0); // dropped block → air
        for (int i = 0; i < Volume; i++)
            raw[i] = palette[s.ReadVarInt()];

        int stateCount = s.ReadVarInt();
        for (int i = 0; i < stateCount; i++) {
            int cell = s.ReadVarInt();
            var block = BlockRegistry.FromState(raw[cell]);
            var state = new BlockState(block);
            if (block.TryGet<StatesBlockDescriptor>(out var sp))
                foreach (var property in sp.States) state.Set(property, s.ReadVarInt());
            chunk.PutCellState(cell, state);
        }

        int entityCount = s.ReadVarInt();
        for (int i = 0; i < entityCount; i++)
            chunk.SetBlockEntity(ReadEntity(s));

        chunk.ClearDirty(); // freshly loaded — the baseline, not a pending change
        return chunk;
    }

    static void WriteEntity(MinecraftStream s, BlockEntity entity) {
        s.WriteLong(entity.Position.X);
        s.WriteLong(entity.Position.Y);
        s.WriteLong(entity.Position.Z);
        s.WriteString(entity.Type.Id.Full);

        if (entity.TryGet<InventoryComponent>(out var inv)) {
            s.WriteBool(true);
            s.WriteVarInt(inv.Size);
            for (int i = 0; i < inv.Size; i++) StackCodec.Write(s, inv[i]);
        } else {
            s.WriteBool(false);
        }
    }

    static BlockEntity ReadEntity(MinecraftStream s) {
        var pos = new Vector3i(s.ReadLong(), s.ReadLong(), s.ReadLong());
        var type = BlockRegistry.FromName(s.ReadString()) ?? BlockRegistry.Air;
        var entity = new BlockEntity(pos, type);

        if (s.ReadBool()) {
            int size = s.ReadVarInt();
            var inv = new InventoryComponent(size);
            for (int i = 0; i < size; i++) inv[i] = StackCodec.Read(s);
            entity.Add(inv);
        }
        return entity;
    }
}
