namespace SharpMinerals.Entities.Components;

/// <summary>Per-tick result of an entity's terrain collision, written by <c>EntityPhysicsSystem</c>; the
/// block-side counterpart to <see cref="CollisionEntityComponent"/>. Lets a system read ground contact
/// rather than infer it from velocity.</summary>
[Component]
public struct BlockCollisionEntityComponent {
    public bool OnGround;
}
