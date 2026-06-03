namespace SharpMinerals.Entities.Components;

/// <summary>An entity's reach box for NON-DIRECT nearby interactions — proximity effects that trigger by being
/// close rather than by aiming, such as item pickup (and, later, things like pressure-plate activation or mob
/// aggro range). It's deliberately larger than the physical <see cref="HitboxEntityComponent"/>: a player's
/// hitbox is 0.6 wide but its pickup reach is wider. The specific interaction is decided by the system + the
/// target's own components (e.g. a <c>PickupEntityComponent</c> on items), not by this box.</summary>
[Component]
public struct InteractionReachEntityComponent {
    public Mfloat Width;
    public Mfloat Height;

    public InteractionReachEntityComponent(Mfloat width, Mfloat height) {
        Width = width;
        Height = height;
    }

    public readonly Mfloat HalfWidth => Width / 2;
}
