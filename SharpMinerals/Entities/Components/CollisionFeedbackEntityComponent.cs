using ArchEntity = Arch.Core.Entity;

namespace SharpMinerals.Entities.Components;

/// <summary>
/// Records which other colliding entities overlap this one each tick. The box itself comes from
/// the entity's <see cref="ColliderEntityComponent"/>; <see cref="Touching"/> is the "feedback" other systems read
/// (e.g. item pickup). The collision pass in <c>World.Tick</c> clears and refills it.
/// <para/>
/// NOTE: deliberately NO parameterless constructor. Parameterless struct constructors are documented
/// as unreliable — the runtime bypasses them on default-init paths (Arch's component arrays / the
/// Release JIT), which left <see cref="Touching"/> null and NRE'd the collision pass. The list is
/// instead set at the (single) creation site via an object initializer — see <c>Player.Spawn</c>.
/// </summary>
public struct CollisionFeedbackEntityComponent {
    /// <summary>Entities overlapping this one as of the last collision pass.</summary>
    public List<ArchEntity> Touching;
}
