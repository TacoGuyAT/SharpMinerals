namespace SharpMinerals.Entities.Components;

/// <summary>What an entity's <see cref="HitboxEntityComponent"/> participates in. A box can serve several at
/// once (or none); systems test the flag rather than the entity type, so e.g. a dropped item collides with
/// terrain (<see cref="Physics"/>) but does NOT block block placement (no <see cref="Placement"/>).</summary>
[Flags]
public enum CollisionUsage : byte {
    None = 0,
    /// <summary>Swept against terrain by the physics step (gravity-driven items, falling blocks).</summary>
    Physics = 1 << 0,
    /// <summary>Obstructs block placement — a player/mob standing in the target cell prevents it.</summary>
    Placement = 1 << 1,
}

/// <summary>An entity's true physical collision box: axis-aligned, centred on its transform in X/Z, rising
/// <see cref="Height"/> from its feet. <see cref="Usage"/> declares which interactions it takes part in. This is
/// the REAL hitbox (e.g. a player is 0.6×1.8) — distinct from <see cref="InteractionReachEntityComponent"/>, the
/// larger proximity box used for nearby interactions like item pickup.</summary>
[Component]
public struct HitboxEntityComponent {
    public Mfloat Width;
    public Mfloat Height;
    public CollisionUsage Usage;

    public HitboxEntityComponent(Mfloat width, Mfloat height, CollisionUsage usage) {
        Width = width;
        Height = height;
        Usage = usage;
    }

    public readonly Mfloat HalfWidth => Width / 2;
}
