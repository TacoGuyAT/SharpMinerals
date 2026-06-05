using SharpMinerals.Persistence;

namespace SharpMinerals.Entities.Components;

/// <summary>Attached to an entity that loaded persistent components whose ids were UNREGISTERED (a removed mod's),
/// carrying their raw <c>[id][blob]</c> so <see cref="EntityCodec"/> re-emits them verbatim on save - re-adding the
/// mod restores them. The entity-side analogue of <c>UnresolvedComponents</c>. Registered (<c>[Component]</c>) so it
/// can be added to a live entity, but NOT itself persistent (the codec pulls its list as the preserved set).</summary>
[Component]
sealed class UnresolvedComponentsEntityComponent(IReadOnlyList<RawComponent> components) {
    public IReadOnlyList<RawComponent> Components { get; } = components;
}
