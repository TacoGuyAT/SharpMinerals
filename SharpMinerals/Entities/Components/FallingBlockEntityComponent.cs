using SharpMinerals.Blocks;
using SharpMinerals.Network.Buffers;

namespace SharpMinerals.Entities.Components;

/// <summary>A block currently falling through the world (sand/gravel that lost its support). The
/// falling-block-specific payload - the carried block + its network id - composed with
/// <see cref="GravityEntityComponent"/> and <see cref="BlockCollisionEntityComponent"/>. Persists the carried
/// block (by name) so a falling block in flight at save resumes its fall (and re-places) on load - never lost.</summary>
[Component]
public struct FallingBlockEntityComponent : IPersistentComponent {
    public BlockType Block;
    /// <summary>Network entity id assigned when announced to clients (0 = not yet announced).</summary>
    public int EntityId;

    public readonly void Write(MinecraftStream s) => s.WriteString(Block.Id.Full);

    public static FallingBlockEntityComponent Read(MinecraftStream s) =>
        new() { Block = BlockType.TryFromPath(s.ReadString(), out var block) ? block : CoreMod.Missing };
}
