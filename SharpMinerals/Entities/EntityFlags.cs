namespace SharpMinerals.Entities;

/// <summary>An entity's "shared flags" state. Bit values match the vanilla entity-metadata flags byte
/// (index 0), so it serializes as <c>(byte)Flags</c>. Sneaking/swimming also drive the modern Pose.</summary>
[Flags]
public enum EntityFlags : byte {
    None         = 0x00,
    OnFire       = 0x01,
    Sneaking     = 0x02,
    Sprinting    = 0x08,
    Swimming     = 0x10, // 1.13+ only; needs server-side water detection to set - not yet driven
    Invisible    = 0x20,
    Glowing      = 0x40,
    FlyingElytra = 0x80,
}
