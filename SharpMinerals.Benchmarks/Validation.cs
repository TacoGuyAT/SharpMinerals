using SharpMinerals.Level.Generator;
using SharpMinerals.Level.Generator.Biomes;

namespace SharpMinerals.Benchmarks;

/// <summary>Guards the benchmark's premise: the two cache strategies must compute the <b>same</b> density field
/// (they share a lattice and the trilinear blend), so any timing difference is the strategy, not different work.
/// Run before the benchmarks; throws if the fields ever diverge. Sampling negative coordinates also checks the
/// shared sign-correct cube indexing (arithmetic shift + mask).</summary>
public static class Validation {
    public static void AssertModesAgree() {
        const int seed = 1337;
        var source = new BiomeSource(seed, BiomeRegistry.Build(seed));
        var inner = new BiomeDensity(seed, source);
        IDensity trilinear = new TrilinearDensity(inner);
        IDensity lattice = new LatticeDensity(inner);
        IDensity columnCached = new ColumnCachedBiomeDensity(seed, source); // must match the stock BiomeDensity

        long checked_ = 0;
        for (int y = 16; y < 144; y += 3)
            for (int z = -20; z < 40; z += 3)
                for (int x = -20; x < 40; x += 3) {
                    double a = trilinear.At(x, y, z), b = lattice.At(x, y, z);
                    if (System.Math.Abs(a - b) > 1e-9)
                        throw new InvalidOperationException(
                            $"interp mismatch at ({x},{y},{z}): trilinear={a}, lattice={b}");

                    double c = inner.At(x, y, z), d = columnCached.At(x, y, z);
                    if (System.Math.Abs(c - d) > 1e-9)
                        throw new InvalidOperationException(
                            $"column-cache mismatch at ({x},{y},{z}): stock={c}, columnCached={d}");
                    checked_++;
                }

        Console.WriteLine($"[validation] Trilinear==Lattice and Stock==ColumnCached across {checked_} samples.");
    }
}
