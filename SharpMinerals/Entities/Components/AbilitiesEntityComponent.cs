namespace SharpMinerals.Entities.Components;

/// <summary>Per-player movement abilities the server drives over the wire: the ability flag bits, the fly speed,
/// and the walk speed. <see cref="Flags"/> mirrors the Player Abilities packet's flag byte; the speeds feed the
/// Player Abilities (fly) and Update Attributes (walk) packets. Transient session state - never persisted, seeded
/// to the creative defaults (may fly, not currently flying).</summary>
[Component]
public sealed class AbilitiesEntityComponent {
    public const byte Invulnerable = 0x01;
    public const byte Flying = 0x02;
    public const byte AllowFlying = 0x04;
    public const byte CreativeMode = 0x08;

    /// <summary>Creative ability set: invulnerable, may fly, instant-break - but not airborne yet.</summary>
    public const byte CreativeDefault = Invulnerable | AllowFlying | CreativeMode; // 0x0D

    public const float DefaultFlyingSpeed = 0.05f; // vanilla baseline fly speed
    public const float DefaultWalkSpeed = 0.1f;    // vanilla baseline generic.movement_speed

    public byte Flags = CreativeDefault;
    public float FlyingSpeed = DefaultFlyingSpeed;
    public float WalkSpeed = DefaultWalkSpeed;
}
