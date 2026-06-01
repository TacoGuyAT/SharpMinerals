using SharpMinerals.Components;
using SharpMinerals.Math;

namespace SharpMinerals.Blocks;

/// <summary>
/// A real, stateful block instance — the "tile entity" for blocks that need per-instance
/// data or behavior (a chest's contents, a TNT's fuse, a block's colour). Plain blocks
/// have no instance and live only as a palette id in the chunk. A ComponentObject, so
/// instance data attaches as components.
/// </summary>
public class BlockEntity : ComponentObject {
    public Vector3i Position { get; }
    public BlockType Type { get; }

    public BlockEntity(Vector3i position, BlockType type) {
        Position = position;
        Type = type;
    }
}
