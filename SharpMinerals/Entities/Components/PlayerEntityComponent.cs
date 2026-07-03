namespace SharpMinerals.Entities.Components;

[Component]
public struct PlayerEntityComponent {
    public const float DefaultFlyingSpeed = 0.05f; // vanilla baseline fly speed

    public ulong ClientId;
    public string Name;
    public Guid Uuid;
    /// <summary>The network entity id other clients use to refer to this player.</summary>
    public int NetId;
    public GameMode GameMode;
    public float FlyingSpeed;
    public float FieldOfViewModifier;
}
