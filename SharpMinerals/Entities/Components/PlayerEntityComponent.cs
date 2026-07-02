namespace SharpMinerals.Entities.Components;

/// <summary>Marks an entity that is backed by a connected client.</summary>
[Component]
public struct PlayerEntityComponent {
    public ulong ClientId;
    public string Name;
    public Guid Uuid;
    /// <summary>The network entity id other clients use to refer to this player.</summary>
    public int NetId;
    public GameMode GameMode;
}
