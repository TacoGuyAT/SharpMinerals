using SharpMinerals.Blocks;
using SharpMinerals.Math;

namespace SharpMinerals.Level;

/// <summary>
/// A cuboid 16×16×16 section of the world. Unlike vanilla's tall 16×384×16 columns,
/// SharpMinerals uses uniform cubes addressed by a 3D <see cref="Vector3i"/> chunk
/// coordinate. Block states are stored densely as ids into the
/// <see cref="BlockRegistry"/> (0 = air).
/// </summary>
public class Chunk : ITickable {
    public const int Size = 16;
    const int Volume = Size * Size * Size;

    readonly ushort[] states = new ushort[Volume]; // defaults to all-air (id 0)

    // Per-instance block data (chests, TNT fuses, …), keyed by world position. Only
    // blocks that need state live here; plain blocks are just an id in `states`.
    readonly Dictionary<Vector3i, BlockEntity> blockEntities = new();

    /// <summary>This chunk's coordinate in chunk space (world = chunk * 16 + local).</summary>
    public Vector3i Position { get; }

    public Chunk(Vector3i position) => Position = position;

    static int Index(int x, int y, int z) => x + z * Size + y * Size * Size;

    public ushort GetState(int x, int y, int z) => states[Index(x, y, z)];
    public void SetState(int x, int y, int z, ushort state) => states[Index(x, y, z)] = state;

    public BlockType GetBlock(int x, int y, int z) => BlockRegistry.FromState(GetState(x, y, z));
    public void SetBlock(int x, int y, int z, BlockType block) => SetState(x, y, z, (ushort)block.Id);

    /// <summary>The block entity at a world position, or null if that block has no instance data.</summary>
    public BlockEntity? GetBlockEntity(Vector3i worldPos) => blockEntities.GetValueOrDefault(worldPos);
    public void SetBlockEntity(BlockEntity entity) => blockEntities[entity.Position] = entity;
    public bool RemoveBlockEntity(Vector3i worldPos) => blockEntities.Remove(worldPos);

    public void Tick() {
        // No block ticking yet — random ticks / block entities land with world systems.
    }
}
