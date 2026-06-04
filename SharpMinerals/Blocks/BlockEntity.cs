using SharpMinerals.Components;
using SharpMinerals.Math;

namespace SharpMinerals.Blocks;

/// <summary>A stateful block instance - the "tile entity" for blocks needing per-instance data (a chest's
/// contents, a TNT fuse). Plain blocks have no instance, just a palette id. Instance data attaches as components.</summary>
public class BlockEntity : ComponentObject {
    public Vector3i Position { get; }
    public BlockType Type { get; }

    public BlockEntity(Vector3i position, BlockType type) {
        Position = position;
        Type = type;
    }
}
