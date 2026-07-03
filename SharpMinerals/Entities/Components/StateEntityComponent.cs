namespace SharpMinerals.Entities.Components;

[Component]
public struct StateEntityComponent {
    public const float DefaultWalkSpeed = 0.1f;    // vanilla baseline generic.movement_speed

    public EntityState State;
    public float WalkingSpeed;
}
