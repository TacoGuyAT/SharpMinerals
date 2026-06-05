namespace SharpMinerals.Entities.Components;

/// <summary>Per-player record of the network entity ids currently SPAWNED on its client (the entities it can see),
/// so the entity tracker can diff visibility each tick and send only the spawn/despawn transitions. Transient -
/// never persisted. See <c>Level.Systems.EntityTrackerSystem</c>.</summary>
[Component]
public sealed class EntityTrackerComponent {
    public readonly HashSet<int> Sent = [];
}
