namespace SharpMinerals.Entities;

[Flags]
public enum PlayerFlags {
    None = 0,
    CanTakeDamage = 1 << 0,
    CanBreakBlocks = 1 << 1,
    CanPlaceBlocks = 1 << 2,
    InstantBreak = 1 << 3,
    HasCollision = 1 << 4,
    CanFly = 1 << 6,
    Invulnerable = 1 << 7,
    NoClip = 1 << 8,
    CreativeMode = 1 << 9,
}