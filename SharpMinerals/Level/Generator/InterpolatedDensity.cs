namespace SharpMinerals.Level.Generator;

/// <summary>Wraps a density field and samples it on a coarse grid (every <see cref="Step"/> blocks),
/// trilinearly interpolating the cells in between - the classic worldgen speed-up. A 16x16x16 cube costs a
/// 5x5x5 = 125 sample lattice instead of 4096 evaluations (~33x fewer), and because the lattice is aligned to
/// a global Step grid (cube size is a multiple of Step), neighbouring cubes share their boundary samples, so
/// the interpolated field stays continuous across cube borders - no seams. Interpolating a linear field is
/// exact; the small error elsewhere is the smoothing that gives older Minecraft terrain its look.
///
/// A per-thread two-cube cache makes the dispatcher's per-cell <see cref="At"/> calls (and the surface pass's
/// upward re-probes, which spill into the cube above near the top) reuse one built lattice. Deterministic and
/// thread-safe: the cache only memoises pure samples, so the result never depends on call order.</summary>
public sealed class InterpolatedDensity : IDensity {
    const int Step = 4;
    const int Lattice = (int)Chunk.Size / Step + 1; // 5 lattice points per axis (0,4,8,12,16)

    readonly IDensity inner;
    readonly ThreadLocal<Slab[]> cache;

    public InterpolatedDensity(IDensity inner) {
        this.inner = inner;
        cache = new ThreadLocal<Slab[]>(() => new[] { new Slab(), new Slab() });
    }

    public double At(int x, int y, int z) {
        int cx = x >> Chunk.Shifts, cy = y >> Chunk.Shifts, cz = z >> Chunk.Shifts;
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

        return slab.Values[(int)Chunk.Index(x & Chunk.Mask, y & Chunk.Mask, z & Chunk.Mask)];
    }

    static double Lerp(double a, double b, double t) => a + (b - a) * t;

    /// <summary>One built cube: its coordinate plus the interpolated 16x16x16 density values.</summary>
    sealed class Slab {
        public readonly double[] Values = new double[Chunk.Size * Chunk.Size * Chunk.Size];
        int cx, cy, cz;
        bool valid;

        public bool Matches(int x, int y, int z) => valid && x == cx && y == cy && z == cz;

        public void Build(IDensity inner, int cubeX, int cubeY, int cubeZ) {
            int baseX = cubeX << Chunk.Shifts, baseY = cubeY << Chunk.Shifts, baseZ = cubeZ << Chunk.Shifts;

            // Sample the coarse lattice once: index (i,j,k) -> (x,y,z). Includes the +16 boundary points so the
            // top cells interpolate against the next cube's shared samples.
            Span<double> l = stackalloc double[Lattice * Lattice * Lattice];
            for (int i = 0; i < Lattice; i++)
                for (int j = 0; j < Lattice; j++)
                    for (int k = 0; k < Lattice; k++)
                        l[(i * Lattice + j) * Lattice + k] =
                            inner.At(baseX + i * Step, baseY + j * Step, baseZ + k * Step);

            for (int ly = 0; ly < Chunk.Size; ly++) {
                int j = ly / Step; double ty = (ly % Step) / (double)Step;
                for (int lz = 0; lz < Chunk.Size; lz++) {
                    int k = lz / Step; double tz = (lz % Step) / (double)Step;
                    for (int lx = 0; lx < Chunk.Size; lx++) {
                        int i = lx / Step; double tx = (lx % Step) / (double)Step;

                        double c000 = l[((i) * Lattice + j) * Lattice + k];
                        double c100 = l[((i + 1) * Lattice + j) * Lattice + k];
                        double c010 = l[((i) * Lattice + j + 1) * Lattice + k];
                        double c110 = l[((i + 1) * Lattice + j + 1) * Lattice + k];
                        double c001 = l[((i) * Lattice + j) * Lattice + k + 1];
                        double c101 = l[((i + 1) * Lattice + j) * Lattice + k + 1];
                        double c011 = l[((i) * Lattice + j + 1) * Lattice + k + 1];
                        double c111 = l[((i + 1) * Lattice + j + 1) * Lattice + k + 1];

                        double x00 = Lerp(c000, c100, tx), x10 = Lerp(c010, c110, tx);
                        double x01 = Lerp(c001, c101, tx), x11 = Lerp(c011, c111, tx);
                        double y0 = Lerp(x00, x10, ty), y1 = Lerp(x01, x11, ty);
                        Values[(int)Chunk.Index(lx, ly, lz)] = Lerp(y0, y1, tz);
                    }
                }
            }

            cx = cubeX; cy = cubeY; cz = cubeZ; valid = true;
        }
    }
}
