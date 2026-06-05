namespace SharpMinerals.Components;

/// <summary>Attached to a block entity whose stored type id was UNREGISTERED on load (so it degraded to the
/// missing placeholder), carrying the original id. Its presence marks the entity as needing recovery; the id
/// round-trips on save so re-adding the mod restores it. See world recovery / <c>ChunkCodec</c>.</summary>
public sealed class UnresolvedTypeComponent(string id) {
    public string Id { get; } = id;
}
