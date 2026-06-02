using SharpMinerals.Blocks;
using SharpMinerals.Math;

namespace SharpMinerals.Level;

/// <summary>A cuboid 16×16×16 section of the world, addressed by a 3D <see cref="Vector3i"/> chunk
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

    public Dictionary<Vector3i, BlockEntity> BlockEntities => blockEntities;

    /// <summary>This chunk's coordinate in chunk space (world = chunk * 16 + local).</summary>
    public Vector3i Position { get; }

    public bool Dirty { get; private set; }

    public Chunk(Vector3i position) => Position = position;

    /// <summary>Marks the chunk clean — its current contents are the persisted baseline.</summary>
    public void ClearDirty() => Dirty = false;

    static Mint Index(Mint x, Mint y, Mint z) => x + z * Size + y * Size * Size;

    public ushort GetState(Mint x, Mint y, Mint z) => states[Index(x, y, z)];
    public void SetState(Mint x, Mint y, Mint z, ushort state) { states[Index(x, y, z)] = state; Dirty = true; }

    // ── Serialization access (same-assembly persistence codec) ───────────────
    internal ushort[] RawStates => states;
    internal IReadOnlyDictionary<int, BlockState> CellStates => blockStates;
    internal void PutCellState(int cellIndex, BlockState state) => blockStates[cellIndex] = state;
    internal IReadOnlyCollection<BlockEntity> Entities => blockEntities.Values;

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
