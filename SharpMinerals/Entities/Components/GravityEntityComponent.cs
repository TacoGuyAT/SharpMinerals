namespace SharpMinerals.Entities.Components;

/// <summary>Marks an entity as subject to gravity — <c>EntityPhysicsSystem</c> applies the per-tick downward
/// pull. Opt-in; shared by dropped items and falling blocks.</summary>
[Component]
public struct GravityEntityComponent { }
