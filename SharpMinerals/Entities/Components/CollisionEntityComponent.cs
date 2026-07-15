using ArchEntity = Arch.Core.Entity;

namespace SharpMinerals.Entities.Components;

/// <summary>Records which other entities overlap this one each tick (read by e.g. item pickup); the collision
/// pass clears and refills <see cref="Touching"/>. The list is a per-tick scratch buffer owned by
/// <see cref="Level.Systems.CollisionFeedbackSystem"/>, which lazily creates it (its only writer) - so no
/// construction-time init is needed and it stays correct however the component is created.</summary>
[Component]
public struct CollisionEntityComponent {
    public List<ArchEntity> Touching;
}
