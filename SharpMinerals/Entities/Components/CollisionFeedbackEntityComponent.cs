using ArchEntity = Arch.Core.Entity;

namespace SharpMinerals.Entities.Components;

/// <summary>Records which other entities overlap this one each tick (read by e.g. item pickup); the collision
/// pass clears and refills <see cref="Touching"/>. Deliberately NO parameterless ctor — those are bypassed on
/// Arch default-init paths, leaving <see cref="Touching"/> null; it's set via object initializer in <c>Player.Spawn</c>.</summary>
[Component]
public struct CollisionFeedbackEntityComponent {
    public List<ArchEntity> Touching;
}
