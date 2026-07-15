using SharpMinerals.Math;

namespace SharpMinerals.Level.Generator.Biomes;

/// <summary>Carves rare, deep, narrow ravines (chasms) into a base terrain density at full (per-column) resolution,
/// so their sharp vertical walls stay crisp - the same reason rivers are kept out of the smooth terrain interpolator
/// (see <see cref="Carve{TCol}"/>). A low-frequency <c>region</c> field gates where ravines may occur (so they are
/// rare and clustered, not everywhere), and a sinuous <c>line</c> field's near-zero contour is the chasm centre;
/// cells within a thin band of it open down to a deep floor, with smoothstepped walls. The horizontal footprint and
/// the opening height are constant down a column, so they are computed once per column and reused for the whole
/// vertical stack. Because the region gate is rare AND very low-frequency, whole cubes clear of it are skipped by
/// <see cref="CubeMayCarve"/> - the per-column footprint is never built there. Below sea level a carved cell floods
/// via the water pass, so a ravine pools water at its bottom and stays open air above.</summary>
public sealed class RavineDensity : Carve<RavineDensity.Column> {
    const double Floor = 11.0;            // deepest the chasm reaches
    const double BottomFade = 5.0;        // round the floor off over this many blocks (no razor bottom)
    const double TopLip = 1.0;            // stop a hair below the natural surface so the rim isn't shaved flat
    const double InnerHalfWidth = 0.018;  // |line| within this is the fully open void core
    const double OuterHalfWidth = 0.045;  // ... smoothstepping out to solid wall by here
    const double RarityThreshold = 0.5;   // only the upper tail of the region field spawns a ravine (rare, clustered)
    const double RarityFade = 0.1;        // feather the region edge so zones fade in rather than snap

    const double AirDensity = -1.0;       // density a fully carved cell collapses to (reads as air)

    // A safe upper bound on how much the low-frequency region field can rise between a cube's corner and its
    // interior (its wavelength ~625 blocks dwarfs a 16-block cube). The coarse skip subtracts this so it never
    // skips a cube whose interior crosses the rarity threshold - a missed ravine would seam at the cube border.
    const double RegionMargin = 0.1;

    readonly BiomeDensity natural;        // river-free surface the chasm opens up to
    readonly NoiseSampler line;           // sinuous path; its near-zero contour is the chasm centre
    readonly NoiseSampler region;         // low-frequency mask; ravines only where this is high

    public RavineDensity(IDensity inner, BiomeDensity natural, int seed) : base(inner) {
        this.natural = natural;
        line = new NoiseSampler(seed ^ 0x4A5F, frequency: 0.0035, octaves: 2);
        region = new NoiseSampler(seed ^ 0x9C3D, frequency: 0.0016, octaves: 2);
    }

    /// <summary>Per-column carve: the 0..1 horizontal opening (0 = solid wall / no ravine) and the (lipped) top.</summary>
    public readonly record struct Column(double Width, double Top);

    // Skip a whole cube when its region field stays below the rarity threshold across it. region barely moves over
    // a 16-block cube, so the max over its four corners (minus a margin for the interior) bounds it safely.
    protected override bool CubeMayCarve(int cubeX, int cubeZ) {
        int bx = cubeX << Chunk.Shifts, bz = cubeZ << Chunk.Shifts, last = (int)Chunk.Size - 1;
        double max = System.Math.Max(
            System.Math.Max(region.Sample2D(bx, bz), region.Sample2D(bx + last, bz)),
            System.Math.Max(region.Sample2D(bx, bz + last), region.Sample2D(bx + last, bz + last)));
        return max > RarityThreshold - RegionMargin;
    }

    protected override Column BuildColumn(int x, int z) {
        double gate = MathUtil.Smoothstep((region.Sample2D(x, z) - RarityThreshold) / RarityFade);
        if (gate <= 0.0) return default; // Width 0 => Apply leaves the column alone
        double d = System.Math.Abs(line.Sample2D(x, z));
        if (d >= OuterHalfWidth) return default;
        double width = d <= InnerHalfWidth
            ? gate
            : gate * (1.0 - MathUtil.Smoothstep((d - InnerHalfWidth) / (OuterHalfWidth - InnerHalfWidth)));
        return new Column(width, natural.SurfaceHeight(x, z) - TopLip);
    }

    // Open the cell toward air, walls fading with the carve amount; 0 outside the chasm body leaves terrain intact.
    protected override double Apply(in Column c, double terrain, int y) =>
        Lerp(terrain, AirDensity, c.Width * VerticalFactor(y, c.Top));

    // 1 through the body of the chasm, rounding off to 0 at the floor and stopping at the (lipped) surface.
    static double VerticalFactor(double y, double top) {
        if (y > top || y < Floor) return 0.0;
        return MathUtil.Smoothstep((y - Floor) / BottomFade);
    }
}
