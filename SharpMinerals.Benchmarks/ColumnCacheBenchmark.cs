using BenchmarkDotNet.Attributes;
using SharpMinerals.Level.Generator;
using SharpMinerals.Level.Generator.Biomes;

namespace SharpMinerals.Benchmarks;

/// <summary>Weighs whether caching the per-column profile (continentalness height + blend weights) in
/// <see cref="BiomeDensity"/> earns its keep. <see cref="Inner"/> swaps the stock density for the
/// <see cref="ColumnCachedBiomeDensity"/> variant; the two benchmarks bracket the effect:
///
///   RawFullRes       - drives At() at full 16^3 resolution with NO interpolation cache. This is the column
///                      cache's best case (every Y re-derives the column), i.e. an upper bound on its benefit.
///   ThroughTrilinear - the real pipeline: TrilinearDensity already collapses At() to the 125-point lattice, so
///                      the column cache only touches those few calls. This is the end-to-end impact that decides
///                      whether the engine change is worth it.</summary>
[MemoryDiagnoser]
public class ColumnCacheBenchmark {
    const int Seed = 1337;
    const int CyLow = 2, CyHigh = 8;
    const int MaxProbe = 5;

    [Params("Stock", "ColumnCached")] public string Inner = "Stock";

    IDensity raw = null!;       // the BiomeDensity variant, sampled directly
    IDensity trilinear = null!; // ... wrapped in the production interpolation cache

    [GlobalSetup]
    public void Setup() {
        var source = new BiomeSource(Seed, BiomeRegistry.Build(Seed));
        raw = Inner == "ColumnCached"
            ? new ColumnCachedBiomeDensity(Seed, source)
            : new BiomeDensity(Seed, source);
        trilinear = new TrilinearDensity(raw);
    }

    [Benchmark(Description = "At() full-res, no interp cache (column-cache upper bound)")]
    public double RawFullRes() {
        double acc = 0;
        for (int cy = CyLow; cy <= CyHigh; cy++) {
            int baseY = cy * 16;
            for (int y = 0; y < 16; y++)
                for (int z = 0; z < 16; z++)
                    for (int x = 0; x < 16; x++)
                        acc += raw.At(x, baseY + y, z);
        }
        return acc;
    }

    [Benchmark(Description = "Through TrilinearDensity + surface probes (real pipeline)")]
    public double ThroughTrilinear() {
        double acc = 0;
        for (int cy = CyLow; cy <= CyHigh; cy++) {
            int baseY = cy * 16;
            for (int y = 0; y < 16; y++)
                for (int z = 0; z < 16; z++)
                    for (int x = 0; x < 16; x++) {
                        int wy = baseY + y;
                        double v = trilinear.At(x, wy, z);
                        if (v > 0) {
                            acc += v;
                            for (int k = 1; k <= MaxProbe; k++) {
                                if (trilinear.At(x, wy + k, z) > 0) acc += 1; else break;
                            }
                        }
                    }
        }
        return acc;
    }
}
