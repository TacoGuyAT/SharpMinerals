using SharpMinerals.Level;
using SharpMinerals.Math;

namespace SharpMinerals.Blocks;

/// <summary>A stateful block instance - the "tile entity" for blocks needing per-instance data (a chest's
/// contents, a TNT fuse). Plain blocks have no instance, just a palette id. Instance data attaches as components.</summary>
public class BlockEntity : ComponentObject {
    public World World { get; init; }
    public Vector3i Position { get; }
    public BlockType Type { get; }

    public BlockEntity(World world, Vector3i position, BlockType type) {
        World = world;
        Position = position;
        Type = type;
    }
}
