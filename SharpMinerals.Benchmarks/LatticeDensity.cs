using SharpMinerals.Level;
using SharpMinerals.Level.Generator;

namespace SharpMinerals.Benchmarks;

/// <summary>The alternative to <see cref="TrilinearDensity"/> this benchmark exists to weigh: it caches the
/// coarse <b>lattice</b> (5x5x5 = 125 doubles per cube) and runs the trilinear interpolation per <see cref="At"/>
/// call, instead of caching the fully interpolated 16x16x16 cube (4096 doubles) and reading it back O(1). Same
/// lattice, same Step alignment, same interpolation math, so it produces bit-identical results - the only thing
/// that changes is where the lerp happens (lazily per read vs once at build) and the cache footprint (~32x
/// smaller). Same per-thread two-cube LRU and key scheme as the production version so the eviction behaviour
/// matches and the comparison is apples-to-apples.</summary>
public sealed class LatticeDensity : IDensity {
    const int Step = 4;
    const int Lattice = Size / Step + 1; // 5 lattice points per axis (0,4,8,12,16)
    const int Size = 16;
    const int Mask = 0b1111;
    const int Shifts = 4;

    readonly IDensity inner;
    readonly ThreadLocal<Slab[]> cache;

    public LatticeDensity(IDensity inner) {
        this.inner = inner;
        cache = new ThreadLocal<Slab[]>(() => new[] { new Slab(), new Slab() });
    }

    public double At(int x, int y, int z) {
        int cx = x >> Shifts, cy = y >> Shifts, cz = z >> Shifts;
        var slabs = cache.Value!;

        Slab slab;
        if (slabs[0].Matches(cx, cy, cz)) {
            slab = slabs[0];
        } else if (slabs[1].Matches(cx, cy, cz)) {
            (slabs[0], slabs[1]) = (slabs[1], slabs[0]); // promote to most-recent
            slab = slabs[0];
        } else {
            var lru = slabs[1];
            lru.Build(inner, cx, cy, cz);
            (slabs[0], slabs[1]) = (lru, slabs[0]);
            slab = slabs[0];
        }

        return slab.Sample(x & Mask, y & Mask, z & Mask);
    }

    static double Lerp(double a, double b, double t) => a + (b - a) * t;

    /// <summary>One built cube's coordinate plus only its 5x5x5 coarse lattice - the interpolation is deferred to
    /// <see cref="Sample"/>.</summary>
    sealed class Slab {
        public readonly double[] L = new double[Lattice * Lattice * Lattice];
        int cx, cy, cz;
        bool valid;

        public bool Matches(int x, int y, int z) => valid && x == cx && y == cy && z == cz;

        public void Build(IDensity inner, int cubeX, int cubeY, int cubeZ) {
            int baseX = cubeX << Shifts, baseY = cubeY << Shifts, baseZ = cubeZ << Shifts;
            for (int i = 0; i < Lattice; i++)
                for (int j = 0; j < Lattice; j++)
                    for (int k = 0; k < Lattice; k++)
                        L[(i * Lattice + j) * Lattice + k] =
                            inner.At(baseX + i * Step, baseY + j * Step, baseZ + k * Step);
            cx = cubeX; cy = cubeY; cz = cubeZ; valid = true;
        }

        // The same trilinear blend TrilinearDensity does at build time, run on demand for one cell.
        public double Sample(int lx, int ly, int lz) {
            int i = lx / Step; double tx = (lx % Step) / (double)Step;
            int j = ly / Step; double ty = (ly % Step) / (double)Step;
            int k = lz / Step; double tz = (lz % Step) / (double)Step;

            double c000 = L[((i) * Lattice + j) * Lattice + k];
            double c100 = L[((i + 1) * Lattice + j) * Lattice + k];
            double c010 = L[((i) * Lattice + j + 1) * Lattice + k];
            double c110 = L[((i + 1) * Lattice + j + 1) * Lattice + k];
            double c001 = L[((i) * Lattice + j) * Lattice + k + 1];
            double c101 = L[((i + 1) * Lattice + j) * Lattice + k + 1];
            double c011 = L[((i) * Lattice + j + 1) * Lattice + k + 1];
            double c111 = L[((i + 1) * Lattice + j + 1) * Lattice + k + 1];

            double x00 = Lerp(c000, c100, tx), x10 = Lerp(c010, c110, tx);
            double x01 = Lerp(c001, c101, tx), x11 = Lerp(c011, c111, tx);
            double y0 = Lerp(x00, x10, ty), y1 = Lerp(x01, x11, ty);
            return Lerp(y0, y1, tz);
        }
    }
}
