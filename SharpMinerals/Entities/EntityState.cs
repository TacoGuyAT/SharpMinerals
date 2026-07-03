namespace SharpMinerals.Entities;

/// <summary>https://minecraft.wiki/w/Java_Edition_protocol/Entity_metadata#Entity</summary>
[Flags]
public enum EntityState : byte {
    None         = 0,
    OnFire       = 1 << 0,
    Sneaking     = 1 << 1,
    Flying       = 1 << 2, // Unused in vanilla
    Sprinting    = 1 << 3,
    Swimming     = 1 << 4,
    Invisible    = 1 << 5,
    Glowing      = 1 << 6,
    FlyingElytra = 1 << 7,
}
