namespace SharpMinerals.Level.Generator.Features;

/// <summary>A feature's footprint around its anchor, in blocks: how far it reaches horizontally
/// (<paramref name="Radius"/>) and vertically above (<paramref name="Up"/>) and below (<paramref name="Down"/>)
/// the anchor cell. The placement driver derives its scatter reach and cube-overlap culling from this, so a
/// feature declares its size once instead of the driver being told a separate "reach". A down-facing anchor
/// (a cave ceiling) grows into <paramref name="Down"/>; an ordinary surface feature into <paramref name="Up"/>.</summary>
public readonly record struct Extent(int Radius, int Up, int Down) {
    /// <summary>A single-cell feature that only occupies the cell just above its anchor (ground cover).</summary>
    public static readonly Extent Cover = new(Radius: 0, Up: 1, Down: 0);
}
