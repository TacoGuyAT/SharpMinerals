using SharpMinerals.Math;

namespace SharpMinerals.Level.Generator.Biomes;

/// <summary>Carves rare, deep, narrow ravines (chasms) into a base terrain density at full (per-column) resolution,
/// so their sharp vertical walls stay crisp - the same reason rivers are kept out of the smooth terrain interpolator.
/// A low-frequency <c>region</c> field gates where ravines may occur (so they are rare and clustered, not everywhere),
/// and a sinuous <c>line</c> field's near-zero contour is the chasm centre; cells within a thin band of it are opened
/// down to a deep floor, with smoothstepped walls. The horizontal footprint and the opening height are constant down
/// a column, so they are computed once per column and cached for the whole vertical stack (like <see cref="RiverDensity"/>);
/// only the cheap vertical window is evaluated per cell. Below sea level a carved cell floods via the water pass, so a
/// ravine pools water at its bottom and stays open air above - the stateless water fill can't tell otherwise.</summary>
public sealed class RavineDensity : IDensity {
    const double Floor = 11.0;            // deepest the chasm reaches
    const double BottomFade = 5.0;        // round the floor off over this many blocks (no razor bottom)
    const double TopLip = 1.0;            // stop a hair below the natural surface so the rim isn't shaved flat
    const double InnerHalfWidth = 0.018;  // |line| within this is the fully open void core
    const double OuterHalfWidth = 0.045;  // ... smoothstepping out to solid wall by here
    const double RarityThreshold = 0.5;   // only the upper tail of the region field spawns a ravine (rare, clustered)
    const double RarityFade = 0.1;        // feather the region edge so zones fade in rather than snap

    const double AirDensity = -1.0;       // density a fully carved cell collapses to (reads as air)

    readonly IDensity inner;
    readonly BiomeDensity natural;        // river-free surface the chasm opens up to
    readonly NoiseSampler line;           // sinuous path; its near-zero contour is the chasm centre
    readonly NoiseSampler region;         // low-frequency mask; ravines only where this is high
    readonly ThreadLocal<Slab[]> cache;

    public RavineDensity(IDensity inner, BiomeDensity natural, int seed) {
        this.inner = inner;
        this.natural = natural;
        line = new NoiseSampler(seed ^ 0x4A5F, frequency: 0.0035, octaves: 2);
        region = new NoiseSampler(seed ^ 0x9C3D, frequency: 0.0016, octaves: 2);
        cache = new ThreadLocal<Slab[]>(() => [new Slab(), new Slab()]);
    }

    public double At(int x, int y, int z) {
        int cx = x >> Chunk.Shifts, cz = z >> Chunk.Shifts;
        var slabs = cache.Value!;

        Slab slab;
        if (slabs[0].Matches(cx, cz)) {
            slab = slabs[0];
        } else if (slabs[1].Matches(cx, cz)) {
            (slabs[0], slabs[1]) = (slabs[1], slabs[0]);
            slab = slabs[0];
        } else {
            var lru = slabs[1];
            lru.Build(this, cx, cz);
            (slabs[0], slabs[1]) = (lru, slabs[0]);
            slab = slabs[0];
        }

        int idx = (int)(x & Chunk.Mask) * (int)Chunk.Size + (int)(z & Chunk.Mask);
        double terrain = inner.At(x, y, z);
        double h = slab.Horizontal[idx];
        if (h <= 0.0) return terrain; // no ravine in this column

        double carve = h * VerticalFactor(y, slab.Top[idx] - TopLip);
        if (carve <= 0.0) return terrain;
        return terrain + (AirDensity - terrain) * carve; // open the cell toward air, walls fade with carve
    }

    // 1 through the body of the chasm, rounding off to 0 at the floor and stopping at the (lipped) surface.
    static double VerticalFactor(double y, double top) {
        if (y > top || y < Floor) return 0.0;
        return MathUtil.Smoothstep((y - Floor) / BottomFade);
    }

    // 1 in the open core, smoothstepping to 0 (solid wall) by the outer width; 0 outside a ravine region.
    double Horizontal(int x, int z) {
        double gate = MathUtil.Smoothstep((region.Sample2D(x, z) - RarityThreshold) / RarityFade);
        if (gate <= 0.0) return 0.0;
        double d = System.Math.Abs(line.Sample2D(x, z));
        if (d >= OuterHalfWidth) return 0.0;
        if (d <= InnerHalfWidth) return gate;
        return gate * (1.0 - MathUtil.Smoothstep((d - InnerHalfWidth) / (OuterHalfWidth - InnerHalfWidth)));
    }

    // One column-cube's per-column carve footprint + opening height, computed once and reused down the stack.
    sealed class Slab {
        public readonly double[] Horizontal = new double[Chunk.Size * Chunk.Size];
        public readonly double[] Top = new double[Chunk.Size * Chunk.Size];
        int cx, cz;
        bool valid;

        public bool Matches(int x, int z) => valid && x == cx && z == cz;

        public void Build(RavineDensity r, int cubeX, int cubeZ) {
            int baseX = cubeX << Chunk.Shifts, baseZ = cubeZ << Chunk.Shifts, size = (int)Chunk.Size;
            for (int lx = 0; lx < size; lx++)
                for (int lz = 0; lz < size; lz++) {
                    int wx = baseX + lx, wz = baseZ + lz, idx = lx * size + lz;
                    double h = r.Horizontal(wx, wz);
                    Horizontal[idx] = h;
                    Top[idx] = h > 0.0 ? r.natural.SurfaceHeight(wx, wz) : 0.0; // surface only matters where carving
                }

            cx = cubeX; cz = cubeZ; valid = true;
        }
    }
}
