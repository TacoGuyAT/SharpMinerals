using SharpMinerals.Level;
using SharpMinerals.Level.Generator;
using SharpMinerals.Level.Generator.Biomes;
using SharpMinerals.Math;

namespace SharpMinerals.Benchmarks;

/// <summary>A copy of the engine's <c>BiomeDensity</c> with one change to weigh: the per-column 2D work - the
/// climate blend (the <c>WeightsFor</c> exp() kernel), the continentalness height spline, and the weighted base
/// height + amplitude - is memoised once per column instead of recomputed for every Y. Only the genuinely 3D
/// parts stay per cell: the 3D field sample and each biome's detail <c>Contribution</c> (which still needs the
/// cached weights). Same formula and same seed as the engine version, so it produces a bit-identical field;
/// Validation checks that against the real one. This exists only to benchmark whether that column cache is worth
/// adding to the engine.</summary>
public sealed class ColumnCachedBiomeDensity : IDensity {
    const double SeaLevel = WorldDefaults.SeaLevel;

    // Same continentalness -> height spline the engine BiomeDensity uses.
    static readonly Spline ContinentalSpline = new(
        (-1.0, -30.0), (-0.3, -8.0), (0.0, 2.0), (0.3, 10.0), (0.7, 22.0), (1.0, 30.0));

    readonly BiomeSource source;
    readonly NoiseSampler density3d;
    readonly int biomeCount;
    readonly ThreadLocal<ColumnCache> cache;

    public ColumnCachedBiomeDensity(int seed, BiomeSource source) {
        this.source = source;
        density3d = new NoiseSampler(seed ^ 0x6F1E9A, frequency: 0.0125, octaves: 4);
        biomeCount = source.Biomes.Count;
        cache = new ThreadLocal<ColumnCache>(() => new ColumnCache(biomeCount));
    }

    public double At(int x, int y, int z) {
        var col = cache.Value!;
        col.Get(x, z, source, out double surface, out double amplitude, out var weights);

        double contribution = 0.0;
        for (int i = 0; i < biomeCount; i++)
            if (source.Biomes[i].Contribution is { } detail) contribution += weights[i] * detail.At(x, y, z);

        return (surface - y) + density3d.Sample3D(x, y, z) * amplitude + contribution;
    }

    /// <summary>Per-thread, direct-mapped memo of a column's derived profile: its surface height, 3D-field
    /// amplitude, and blend weights. Same shape as the engine's climate cache, one layer up (it caches the
    /// derived height/weights, not the raw climate samples). Memo-only and pure, so a collision just recomputes.</summary>
    sealed class ColumnCache {
        const int Bits = 12;
        const int Size = 1 << Bits;

        readonly int bc;
        readonly long[] keys = new long[Size];
        readonly bool[] occupied = new bool[Size];
        readonly double[] surf = new double[Size];
        readonly double[] amp = new double[Size];
        readonly double[] wbuf; // Size blocks of bc weights, laid out flat

        public ColumnCache(int biomeCount) {
            bc = biomeCount;
            wbuf = new double[Size * biomeCount];
        }

        public void Get(int x, int z, BiomeSource source,
                        out double surface, out double amplitude, out ReadOnlySpan<double> weights) {
            long key = ((long)(uint)x << 32) | (uint)z;
            int slot = Slot(key);

            if (occupied[slot] && keys[slot] == key) {
                surface = surf[slot];
                amplitude = amp[slot];
                weights = wbuf.AsSpan(slot * bc, bc);
                return;
            }

            var climate = source.ClimateAt(x, z);
            Span<double> w = stackalloc double[bc];
            source.WeightsFor(climate, w);

            double baseHeight = 0.0, ampl = 0.0;
            for (int i = 0; i < bc; i++) {
                baseHeight += w[i] * source.Biomes[i].BaseHeight;
                ampl += w[i] * source.Biomes[i].HeightVariation;
            }
            ampl *= 1.0 + 0.6 * System.Math.Max(0.0, climate.Rockiness);
            double surf_ = SeaLevel + ContinentalSpline.Sample(climate.Continentalness) + baseHeight;

            keys[slot] = key;
            occupied[slot] = true;
            surf[slot] = surf_;
            amp[slot] = ampl;
            w.CopyTo(wbuf.AsSpan(slot * bc, bc));

            surface = surf_;
            amplitude = ampl;
            weights = wbuf.AsSpan(slot * bc, bc);
        }

        static int Slot(long key) => (int)((ulong)key * 0x9E3779B97F4A7C15UL >> (64 - Bits));
    }
}
