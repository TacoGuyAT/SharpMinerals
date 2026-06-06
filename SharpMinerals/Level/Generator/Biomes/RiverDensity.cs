using SharpMinerals.Math;

namespace SharpMinerals.Level.Generator.Biomes;

/// <summary>Carves rivers into a base terrain density at full (per-column) resolution, so the banks stay crisp -
/// the river is a thin, sharp feature the general terrain interpolator would alias, so it is kept out of that
/// path and evaluated directly here. The carve is 2D (constant down a column), so each column's carved surface
/// and blend are computed once and cached for the whole vertical stack rather than recomputed per cell; that
/// keeps the (expensive, climate-derived) natural surface cost off the hot 3D loop. Away from rivers a cell is
/// the base unchanged; inside a channel it blends toward a flat carved bed, which the water fill then floods.</summary>
public sealed class RiverDensity : IDensity {
    const double SeaLevel = WorldDefaults.SeaLevel;
    const double RiverWidth = 0.04;             // |river| band that carves; the water channel is the inner part
    const double RiverBed = SeaLevel - 4.0;     // deepest the channel carves to (water then fills it to sea level)
    const double RiverHighlandStart = 4.0;      // ease rivers off once the natural surface is this high above sea level
    const double RiverHighlandFade = 10.0;      // ... fading to no influence this many blocks higher (highlands keep shape)

    readonly IDensity baseTerrain;
    readonly BiomeSource source;
    readonly BiomeDensity natural; // river-free surface the carve lowers from
    readonly ThreadLocal<Slab[]> cache;

    public RiverDensity(IDensity baseTerrain, BiomeSource source, BiomeDensity natural) {
        this.baseTerrain = baseTerrain;
        this.source = source;
        this.natural = natural;
        cache = new ThreadLocal<Slab[]>(() => new[] { new Slab(), new Slab() });
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
            lru.Build(source, natural, cx, cz);
            (slabs[0], slabs[1]) = (lru, slabs[0]);
            slab = slabs[0];
        }

        int idx = (int)(x & Chunk.Mask) * (int)Chunk.Size + (int)(z & Chunk.Mask);
        double blend = slab.Blend[idx];
        double terrain = baseTerrain.At(x, y, z);
        if (blend <= 0.0) return terrain; // no river in this column

        double channel = slab.Carved[idx] - y; // density of a flat bed at the carved height (flattens bed/banks too)
        return terrain + (channel - terrain) * blend;
    }

    // 1 at the river's centre, easing to 0 at the band edge (smoothstep banks); 0 well away from any river.
    static double RiverFactor(double river) {
        double t = System.Math.Abs(river) / RiverWidth;
        return t >= 1.0 ? 0.0 : 1.0 - MathUtil.Smoothstep(t);
    }

    // Fades rivers out as the surface rises above sea level, so they carve lowlands but leave highlands intact.
    static double HighlandAttenuation(double naturalSurface) =>
        1.0 - MathUtil.Smoothstep((naturalSurface - (SeaLevel + RiverHighlandStart)) / RiverHighlandFade);

    // One column-cube's per-column carve, computed full-resolution once and reused down the whole vertical stack.
    sealed class Slab {
        public readonly double[] Carved = new double[Chunk.Size * Chunk.Size];
        public readonly double[] Blend = new double[Chunk.Size * Chunk.Size];
        int cx, cz;
        bool valid;

        public bool Matches(int x, int z) => valid && x == cx && z == cz;

        public void Build(BiomeSource source, BiomeDensity natural, int cubeX, int cubeZ) {
            int baseX = cubeX << Chunk.Shifts, baseZ = cubeZ << Chunk.Shifts, size = (int)Chunk.Size;
            for (int lx = 0; lx < Chunk.Size; lx++)
                for (int lz = 0; lz < Chunk.Size; lz++) {
                    double nat = natural.SurfaceHeight(baseX + lx, baseZ + lz);
                    double blend = RiverFactor(source.River(baseX + lx, baseZ + lz)) * HighlandAttenuation(nat);
                    int idx = lx * size + lz;
                    Blend[idx] = blend;
                    Carved[idx] = nat - System.Math.Max(0.0, nat - RiverBed) * blend;
                }

            cx = cubeX; cz = cubeZ; valid = true;
        }
    }
}
