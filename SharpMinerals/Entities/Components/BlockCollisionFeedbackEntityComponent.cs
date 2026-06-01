namespace SharpMinerals.Entities.Components;

/// <summary>
/// Per-tick result of an entity's terrain (block) collision, written by <c>EntityPhysicsSystem</c> — the
/// block-side counterpart to <see cref="CollisionFeedbackEntityComponent"/> (entity-vs-entity). A system
/// reads it instead of re-deriving ground contact: a falling block lands when <see cref="OnGround"/> is
/// set (a robust signal, unlike inferring it from velocity reaching zero).
/// </summary>
public struct BlockCollisionFeedbackEntityComponent {
    /// <summary>Whether the entity rested on a solid floor as of the last physics step.</summary>
    public bool OnGround;
}
