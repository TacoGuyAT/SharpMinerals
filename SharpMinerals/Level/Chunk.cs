using SharpMinerals.Blocks;
using SharpMinerals.Components;
using SharpMinerals.Math;

namespace SharpMinerals.Level;

/// <summary>The recovery record for one cell whose stored block id was UNREGISTERED on load: the cell holds
/// <see cref="BlockRegistry.Missing"/>, but the original id, its state SCHEMA signature (for by-name migration),
/// and the raw state values are kept so they write back verbatim - re-adding the mod restores the block - and so
/// recovery can remap it. See <c>ChunkCodec</c> / <c>StateSchema</c>.</summary>
sealed class UnresolvedBlock(string id, string schema) {
    public string Id { get; } = id;
    public string Schema { get; } = schema;
    public int[]? State { get; set; }
}

/// <summary>A cuboid 16x16x16 section of the world, addressed by a 3D <see cref="Vector3i"/> chunk
/// coordinate (uniform cubes, not vanilla's tall columns). Block states are stored densely as ids into
/// the <see cref="BlockRegistry"/> (0 = air).</summary>
public class Chunk : ITickable {
    public const Mint Mask = 0b1111;
    public const int Shifts = 4;
    public const Mint Size = 1 << Shifts;
    const Mint Volume = Size * Size * Size;

    readonly ushort[] states = new ushort[Volume];
    readonly Dictionary<Vector3i, BlockEntity> blockEntities = [];
    readonly List<ITickable> tickingBlockEntities = [];
    readonly Dictionary<int, BlockState> blockStates = []; // keyed by local cell index, not world position

    // Cells whose stored block id was UNREGISTERED on load (id + preserved schema + raw state), keyed by cell. A
    // block cell has no component host like a block entity does, so this side table is where its recovery data
    // lives. Empty for a normally-loaded chunk. See UnresolvedBlock / ChunkCodec / world recovery.
    readonly Dictionary<int, UnresolvedBlock> unresolved = [];

    public Dictionary<Vector3i, BlockEntity> BlockEntities => blockEntities;

    /// <summary>This chunk's coordinate in chunk space (world = chunk * 16 + local).</summary>
    public Vector3i Position { get; }

    public bool Dirty { get; private set; }

    public Chunk(Vector3i position) => Position = position;

    /// <summary>Marks the chunk clean - its current contents are the persisted baseline.</summary>
    public void ClearDirty() => Dirty = false;

    /// <summary>Marks the chunk dirty so it saves on the next pass. The block edit paths do this automatically;
    /// call it after mutating a block entity's components in place (e.g. a machine's stored energy).</summary>
    public void MarkDirty() => Dirty = true;

    static Mint Index(Mint x, Mint y, Mint z) => x + z * Size + y * Size * Size;

    public ushort GetState(Mint x, Mint y, Mint z) => states[Index(x, y, z)];
    public void SetState(Mint x, Mint y, Mint z, ushort state) { states[Index(x, y, z)] = state; Dirty = true; }

    // -- Serialization access (same-assembly persistence codec) ---------------
    internal ushort[] RawStates => states;
    internal IReadOnlyDictionary<int, BlockState> CellStates => blockStates;
    internal void PutCellState(int cellIndex, BlockState state) => blockStates[cellIndex] = state;
    internal IReadOnlyCollection<BlockEntity> Entities => blockEntities.Values;
    internal IReadOnlyDictionary<int, UnresolvedBlock> Unresolved => unresolved;
    internal void MarkUnresolved(int cellIndex, string originalId, string schema) => unresolved[cellIndex] = new UnresolvedBlock(originalId, schema);
    internal void SetUnresolvedState(int cellIndex, int[] values) { if (unresolved.TryGetValue(cellIndex, out var u)) u.State = values; }

    // -- World recovery -------------------------------------------------------
    /// <summary>True if any block (or block entity) here degraded to the <c>missing</c> placeholder because its
    /// stored id is unregistered - i.e. the chunk references content that needs recovery (re-add the mod, or remap).</summary>
    public bool HasUnresolved => unresolved.Count > 0 || blockEntities.Values.Any(e => e.Has<UnresolvedTypeComponent>());

    /// <summary>The unregistered identifiers this chunk degraded to missing (blocks then block entities). May
    /// repeat; callers dedupe. Empty unless <see cref="HasUnresolved"/>.</summary>
    public IEnumerable<string> UnresolvedIds {
        get {
            foreach (var u in unresolved.Values) yield return u.Id;
            foreach (var entity in blockEntities.Values)
                if (entity.TryGet<UnresolvedTypeComponent>(out var type)) yield return type.Id;
        }
    }

    /// <summary>Offline recovery: for each cell/block-entity that degraded to missing whose original id is in
    /// <paramref name="remap"/>, set the cell to the chosen block (migrating its preserved state by name, scrapping
    /// what doesn't fit) or remove the decided unknown block entity (its instance data is unrecoverable without the
    /// original mod). Ids absent from the map are left unresolved (skipped). Returns the number of objects changed.</summary>
    internal int ResolveUnresolved(IReadOnlyDictionary<string, BlockType> remap) {
        int changed = 0;
        foreach (var (cell, u) in unresolved.ToList()) {
            if (!remap.TryGetValue(u.Id, out var block)) continue;
            states[cell] = (ushort)block.BlockId;
            // Carry the preserved state over by NAME where it fits the replacement; scrap what doesn't.
            if (u.State is { } values && StateSchema.Migrate(u.Schema, values, block) is var migrated && !migrated.Matches(new BlockState(block)))
                blockStates[cell] = migrated;
            else
                blockStates.Remove(cell);
            unresolved.Remove(cell);
            changed++;
        }
        foreach (var entity in blockEntities.Values.Where(e => e.TryGet<UnresolvedTypeComponent>(out var t) && remap.ContainsKey(t.Id)).ToList()) {
            RemoveBlockEntity(entity.Position);
            changed++;
        }
        if (changed > 0) Dirty = true;
        return changed;
    }

    public BlockType GetBlock(Mint x, Mint y, Mint z) => BlockRegistry.FromState(GetState(x, y, z));
    public void SetBlock(Mint x, Mint y, Mint z, BlockType block) => SetState(x, y, z, (ushort)block.BlockId);

    /// <summary>The block state at a local cell, or null if it's the type's default (stateless) state.</summary>
    public BlockState? GetBlockState(Mint x, Mint y, Mint z) => blockStates.GetValueOrDefault((int)Index(x, y, z));
    public BlockState? GetBlockState(Vector3i pos) => GetBlockState(pos.X, pos.Y, pos.Z);
    public void SetBlockState(Mint x, Mint y, Mint z, BlockState? state) {
        var i = (int)Index(x, y, z);
        if (state is null) blockStates.Remove(i);
        else blockStates[i] = state;
        Dirty = true;
    }
    public void SetBlockState(Vector3i pos, BlockState? state) => SetBlockState(pos.X, pos.Y, pos.Z, state);

    /// <summary>The block entity at a world position, or null if that block has no instance data.</summary>
    public BlockEntity? GetBlockEntity(Vector3i worldPos) => blockEntities.GetValueOrDefault(worldPos);

    public void SetBlockEntity(BlockEntity entity) {
        if (blockEntities.TryGetValue(entity.Position, out var prev) && prev is ITickable oldTick)
            tickingBlockEntities.Remove(oldTick);
        blockEntities[entity.Position] = entity;
        if (entity is ITickable newTick) tickingBlockEntities.Add(newTick);
        Dirty = true;
    }

    public bool RemoveBlockEntity(Vector3i worldPos) {
        if (blockEntities.TryGetValue(worldPos, out var prev) && prev is ITickable oldTick)
            tickingBlockEntities.Remove(oldTick);
        bool removed = blockEntities.Remove(worldPos);
        if (removed) Dirty = true;
        return removed;
    }

    public void Tick() {
        foreach (var be in tickingBlockEntities)
            be.Tick();
    }
}
