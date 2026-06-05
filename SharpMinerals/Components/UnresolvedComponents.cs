using SharpMinerals.Persistence;

namespace SharpMinerals.Components;

/// <summary>Attached to a <see cref="ComponentObject"/> (a block entity, an item stack's data) that loaded
/// persistent components whose ids were UNREGISTERED (a removed mod's), carrying their raw <c>[id][blob]</c> so they
/// re-emit verbatim on save - re-adding the mod restores them. Transient and non-persistent itself (like
/// <see cref="UnresolvedTypeComponent"/>); its presence just means "carry these forward." See world recovery /
/// <c>ComponentBag</c>.</summary>
sealed class UnresolvedComponents(IReadOnlyList<RawComponent> components) {
    public IReadOnlyList<RawComponent> Components { get; } = components;
}
