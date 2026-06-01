using SharpMinerals.Blocks;

namespace SharpMinerals.Entities.Components;

/// <summary>
/// A block currently falling through the world (sand/gravel that lost its support). Carries the
/// <see cref="BlockType"/> it will re-place when it lands. It composes with the shared
/// <see cref="GravityEntityComponent"/> (the fall, via <c>EntityPhysicsSystem</c>) and
/// <see cref="BlockCollisionFeedbackEntityComponent"/> (ground contact); <c>Level.Systems.FallingBlockSystem</c>
/// reads that feedback to convert it back into a block on landing. This component is just the
/// falling-block-specific payload (the carried block + its network id).
/// </summary>
public struct FallingBlockEntityComponent {
    /// <summary>The block this entity carries — placed back into the world when it lands.</summary>
    public BlockType Block;
    /// <summary>Network entity id assigned when announced to clients (0 = not yet announced).</summary>
    public int EntityId;
}
