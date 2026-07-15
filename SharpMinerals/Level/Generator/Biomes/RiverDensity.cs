using SharpMinerals.Math;

namespace SharpMinerals.Level.Generator.Biomes;

/// <summary>Carves rivers into a base terrain density at full (per-column) resolution, so the banks stay crisp -
/// the river is a thin, sharp feature the general terrain interpolator would alias, so it is kept out of that path
/// and evaluated directly here (see <see cref="Carve{TCol}"/>). The carve is 2D (constant down a column), so each
/// column's carved surface and blend are computed once and reused for the whole vertical stack; that keeps the
/// (expensive, climate-derived) natural surface cost off the hot 3D loop. Away from rivers a cell is the base
/// unchanged; inside a channel it blends toward a flat carved bed, which the water fill then floods.</summary>
public sealed class RiverDensity : Carve<RiverDensity.Column> {
    const double SeaLevel = WorldDefaults.SeaLevel;
    const double RiverWidth = 0.04;             // |river| band that carves; the water channel is the inner part
    const double RiverBed = SeaLevel - 4.0;     // deepest the channel carves to (water then fills it to sea level)
    const double RiverHighlandStart = 4.0;      // ease rivers off once the natural surface is this high above sea level
    const double RiverHighlandFade = 10.0;      // ... fading to no influence this many blocks higher (highlands keep shape)

    readonly BiomeSource source;
    readonly BiomeDensity natural; // river-free surface the carve lowers from

    public RiverDensity(IDensity baseTerrain, BiomeSource source, BiomeDensity natural) : base(baseTerrain) {
        this.source = source;
        this.natural = natural;
    }

    /// <summary>Per-column carve: the flat carved bed height and the 0..1 blend toward it.</summary>
    public readonly record struct Column(double Carved, double Blend);

    protected override Column BuildColumn(int x, int z) {
        double nat = natural.SurfaceHeight(x, z);
        double blend = RiverFactor(source.River(x, z)) * HighlandAttenuation(nat);
        return new Column(nat - System.Math.Max(0.0, nat - RiverBed) * blend, blend);
    }

    // A flat bed at the carved height (density carved - y) blended in; blend <= 0 leaves the column untouched.
    protected override double Apply(in Column c, double terrain, int y) => Lerp(terrain, c.Carved - y, c.Blend);

    // 1 at the river's centre, easing to 0 at the band edge (smoothstep banks); 0 well away from any river.
    static double RiverFactor(double river) {
        double t = System.Math.Abs(river) / RiverWidth;
        return t >= 1.0 ? 0.0 : 1.0 - MathUtil.Smoothstep(t);
    }

    // Fades rivers out as the surface rises above sea level, so they carve lowlands but leave highlands intact.
    static double HighlandAttenuation(double naturalSurface) =>
        1.0 - MathUtil.Smoothstep((naturalSurface - (SeaLevel + RiverHighlandStart)) / RiverHighlandFade);
}
