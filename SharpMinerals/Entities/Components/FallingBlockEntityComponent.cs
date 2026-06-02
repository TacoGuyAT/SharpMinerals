using SharpMinerals.Blocks;

namespace SharpMinerals.Entities.Components;

/// <summary>A block currently falling through the world (sand/gravel that lost its support). The
/// falling-block-specific payload — the carried block + its network id — composed with
/// <see cref="GravityEntityComponent"/> and <see cref="BlockCollisionFeedbackEntityComponent"/>.</summary>
public struct FallingBlockEntityComponent {
    public BlockType Block;
    /// <summary>Network entity id assigned when announced to clients (0 = not yet announced).</summary>
    public int EntityId;
}
