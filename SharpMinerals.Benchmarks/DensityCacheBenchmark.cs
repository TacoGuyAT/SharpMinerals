using BenchmarkDotNet.Attributes;
using SharpMinerals.Level.Generator;
using SharpMinerals.Level.Generator.Biomes;

namespace SharpMinerals.Benchmarks;

/// <summary>Weighs the two density-cache strategies against each other on the generator's real access pattern.
/// Both modes wrap the <b>same</b> <see cref="BiomeDensity"/>, so the expensive noise sampling (and the climate
/// cache underneath it) is common-mode: the measured delta is purely the interpolation-cache strategy -
/// <see cref="TrilinearDensity"/> (store the interpolated 16^3 cube, read O(1)) vs <see cref="LatticeDensity"/>
/// (store the 5^3 lattice, interpolate per read).
///
/// The workload mirrors <c>ShaderChunkGenerator</c>: a vertical stack of cubes, each swept y,z,x like the terrain
/// pass. The <see cref="Probes"/> axis toggles the surface pass's upward re-probes (the repeat-access pattern that
/// makes the lattice recompute its lerp), so the two rows decompose the trade-off:
///   Probes=false -> streaming, each cell read once (front-loaded build vs lazy interp; prefetch-friendly)
///   Probes=true  -> cells re-read several times near surfaces (where re-interpolation should cost the lattice)
///
/// Note the region is fixed, so by measurement time the inner climate cache is warm - this isolates the interp
/// layer rather than first-touch noise cost, which is the comparison the strategy choice actually turns on.</summary>
[MemoryDiagnoser]
public class DensityCacheBenchmark {
    const int Seed = 1337;
    const int CyLow = 2, CyHigh = 8; // cube stack y=32..143: underground (solid, probe-heavy) -> surface -> air
    const int MaxProbe = 5;          // SurfaceShader's deepest upward re-probe (one past the deepest biome surface)

    [Params("Trilinear", "Lattice")] public string Mode = "Trilinear";
    [Params(false, true)] public bool Probes;

    IDensity density = null!;

    [GlobalSetup]
    public void Setup() {
        var source = new BiomeSource(Seed, BiomeRegistry.Build(Seed));
        var inner = new BiomeDensity(Seed, source);
        density = Mode == "Lattice" ? new LatticeDensity(inner) : new TrilinearDensity(inner);
    }

    [Benchmark]
    public double GenerateColumn() {
        double acc = 0;
        for (int cy = CyLow; cy <= CyHigh; cy++) {
            int baseY = cy * 16;
            for (int y = 0; y < 16; y++)
                for (int z = 0; z < 16; z++)
                    for (int x = 0; x < 16; x++) {
                        int wy = baseY + y;
                        double v = density.At(x, wy, z); // terrain pass: one read per cell
                        if (v > 0) {
                            acc += v;
                            if (Probes)
                                for (int k = 1; k <= MaxProbe; k++) { // surface pass: re-probe upward over solid cells
                                    if (density.At(x, wy + k, z) > 0) acc += 1; else break;
                                }
                        }
                    }
        }
        return acc; // returned so the JIT cannot elide the work
    }
}
