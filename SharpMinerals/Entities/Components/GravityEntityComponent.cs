namespace SharpMinerals.Entities.Components;

/// <summary>
/// Marks an entity as subject to gravity — <c>EntityPhysicsSystem</c> applies the per-tick downward pull
/// to it. Opt-in (a future projectile with custom motion simply omits it) and the shared building block
/// that both dropped items and falling blocks compose with (see <see cref="PickupEntityComponent"/> /
/// <see cref="FallingBlockEntityComponent"/>).
/// </summary>
public struct GravityEntityComponent { }
