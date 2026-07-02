using SharpMinerals.Blocks;
using SharpMinerals.Blocks.Descriptors;
using SharpMinerals.Components;
using SharpMinerals.Level;
using SharpMinerals.Math;
using SharpMinerals.Network.Buffers;

namespace SharpMinerals.Persistence;

/// <summary>Serializes a <see cref="Chunk"/> to a self-describing <c>byte[]</c>. Uses a chunk-local palette
/// header (distinct block NAMES written once, then each cell references a palette index - compact and
/// id-shift safe), followed by sparse per-cell states and block entities.</summary>
public static class ChunkCodec {
    // Each cell's state is LENGTH-PREFIXED with its value count, so an UNREGISTERED block's state can be
    // skipped/preserved on load without its schema (inferring the length from the block would desync the stream
    // the moment the block is gone). Pre-release: there's no older on-disk format to stay compatible with.
    const byte Version = 1;
    const Mint Volume = Chunk.Size * Chunk.Size * Chunk.Size;

    public static byte[] Serialize(Chunk chunk) {
        using var ms = new MemoryStream();
        var s = new MinecraftStream(ms);
        s.WriteUByte(Version);

        // Palette header: distinct block NAMES, in first-seen order. A cell that loaded as `missing` because its
        // stored id was unregistered writes its ORIGINAL id back (non-destructive: re-adding the mod restores it),
        // so the palette is keyed by the effective name, not the now-collapsed Missing id.
        var raw = chunk.RawStates;
        var unresolved = chunk.Unresolved;
        var indexOf = new Dictionary<string, int>();
        var names = new List<string>();
        var schemas = new List<string>();
        var cellPalette = new int[Volume];
        for (int cell = 0; cell < Volume; cell++) {
            // An unresolved cell writes its ORIGINAL id + schema back (non-destructive); everything else its own.
            bool unknown = unresolved.TryGetValue(cell, out var u);
            string name = unknown ? u!.Id : BlockType.Registry[raw[cell]].Id.Full;
            if (!indexOf.TryGetValue(name, out int index)) {
                index = names.Count; indexOf[name] = index; names.Add(name);
                // Each entry carries its state SCHEMA so saved state can later be migrated by name.
                schemas.Add(unknown ? u!.Schema : StateSchema.Of(BlockType.Registry[raw[cell]]));
            }
            cellPalette[cell] = index;
        }
        s.WriteVarInt(names.Count);
        for (int i = 0; i < names.Count; i++) { s.WriteString(names[i]); s.WriteString(schemas[i]); }

        foreach (var index in cellPalette) s.WriteVarInt(index); // dense cells as palette indices

        // Sparse per-cell block states (chest facing, wool colour, ...), each LENGTH-PREFIXED with its value count
        // so the reader can preserve an unknown block's state without its schema. Includes the preserved raw state
        // of cells that degraded to missing, written back verbatim.
        int unresolvedStateCount = 0;
        foreach (var u in unresolved.Values) if (u.State is not null) unresolvedStateCount++;
        s.WriteVarInt(chunk.CellStates.Count + unresolvedStateCount);
        foreach (var (cell, state) in chunk.CellStates) {
            s.WriteVarInt(cell);
            if (state.Type.TryGet<StatesBlockDescriptor>(out var sp)) {
                s.WriteVarInt(sp.States.Count);
                foreach (var property in sp.States) s.WriteVarInt(state.Get(property));
            } else {
                s.WriteVarInt(0);
            }
        }
        foreach (var (cell, u) in unresolved) {
            if (u.State is not { } values) continue;
            s.WriteVarInt(cell);
            s.WriteVarInt(values.Length);
            foreach (var value in values) s.WriteVarInt(value);
        }

        // Block entities (e.g. a chest's contents).
        s.WriteVarInt(chunk.Entities.Count);
        foreach (var entity in chunk.Entities) WriteEntity(s, entity);

        return ms.ToArray();
    }

    public static Chunk Deserialize(Vector3i position, byte[] data, World world) {
        using var ms = new MemoryStream(data, writable: false);
        var s = new MinecraftStream(ms);
        if (s.ReadUByte() is var version && version != Version)
            throw new NotSupportedException($"Unknown chunk format version {version}.");

        var chunk = new Chunk(position);
        var raw = chunk.RawStates;

        int paletteCount = s.ReadVarInt();
        var palette = new ushort[paletteCount];
        var unresolvedName = new string?[paletteCount];  // original id for entries that don't resolve
        var unresolvedSchema = new string[paletteCount]; // ...and its preserved schema (for migration on remap/return)
        var migrate = new Dictionary<ushort, string>();  // known block id -> OLD schema, when it drifted since save
        for (int i = 0; i < paletteCount; i++) {
            var name = s.ReadString();
            var schema = s.ReadString();
            if (BlockType.TryFromPath(name, out var block)) {
                palette[i] = (ushort)block.BlockId;
                if (schema != StateSchema.Of(block)) migrate[(ushort)block.BlockId] = schema; // schema changed -> migrate by name
            } else {
                palette[i] = (ushort)CoreMod.Missing.BlockId; // dropped block -> missing (id kept for recovery)
                unresolvedName[i] = name;
                unresolvedSchema[i] = schema;
            }
        }
        for (int i = 0; i < Volume; i++) {
            int index = s.ReadVarInt();
            raw[i] = palette[index];
            if (unresolvedName[index] is { } original)
                chunk.MarkUnresolved(i, original, unresolvedSchema[index]);
        }

        int stateCount = s.ReadVarInt();
        for (int i = 0; i < stateCount; i++) {
            int cell = s.ReadVarInt();
            // Length-prefixed: read the whole payload regardless of the block, so an unknown block never desyncs.
            int valueCount = s.ReadVarInt();
            var values = new int[valueCount];
            for (int v = 0; v < valueCount; v++) values[v] = s.ReadVarInt();
            if (chunk.Unresolved.ContainsKey(cell)) {
                chunk.SetUnresolvedState(cell, values); // unknown block: keep its raw state for later migration
            } else if (migrate.TryGetValue(raw[cell], out var oldSchema)) {
                chunk.PutCellState(cell, StateSchema.Migrate(oldSchema, values, BlockType.Registry[raw[cell]])); // schema drifted
            } else {
                var block = BlockType.Registry[raw[cell]];
                var state = new BlockState(block);
                if (block.TryGet<StatesBlockDescriptor>(out var sp))
                    for (int p = 0; p < sp.States.Count && p < values.Length; p++) state.Set(sp.States[p], values[p]);
                chunk.PutCellState(cell, state);
            }
        }

        int entityCount = s.ReadVarInt();
        for (int i = 0; i < entityCount; i++)
            chunk.SetBlockEntity(ReadEntity(s, world));

        chunk.ClearDirty(); // freshly loaded - the baseline, not a pending change
        return chunk;
    }

    static void WriteEntity(MinecraftStream s, BlockEntity entity) {
        s.WriteLong(entity.Position.X);
        s.WriteLong(entity.Position.Y);
        s.WriteLong(entity.Position.Z);
        s.WriteString(entity.TryGet<UnresolvedTypeComponent>(out var u) ? u.Id : entity.Type.Id.Full); // restore an unregistered type
        ComponentBag.Write(s, entity); // its persistent components (inventory, ...) as a length-prefixed bag
    }

    static BlockEntity ReadEntity(MinecraftStream s, World world) {
        var pos = new Vector3i(s.ReadLong(), s.ReadLong(), s.ReadLong());
        var name = s.ReadString();
        BlockEntity entity;
        if(BlockType.TryFromPath(name, out var resolved)) {
            entity = new BlockEntity(world, pos, resolved);
        } else {
            entity = new BlockEntity(world, pos, CoreMod.Missing);
            entity.Add(new UnresolvedTypeComponent(name));
        }
        ComponentBag.Read(s, entity);
        return entity;
    }
}
