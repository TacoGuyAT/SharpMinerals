using SharpMinerals.Math;

namespace SharpMinerals.Level.Generator;

/// <summary>Like <see cref="TrilinearDensity"/> but tricubic (Catmull-Rom) instead of trilinear: it samples
/// the same coarse <see cref="Step"/> grid yet fits smooth C1 curves through it, so sharp features (river banks)
/// come out rounded rather than faceted. It needs one extra lattice point on each side - a 7x7x7 = 343 sample
/// grid per cube (~2.7x the trilinear 125, still far under 4096 per-cell). The lattice is global-Step-aligned and
/// Catmull-Rom is C1 across shared knots, so cube borders stay seamless, and it reproduces linear fields exactly.
/// Same per-thread two-cube cache and determinism as the trilinear version.</summary>
public sealed class TricubicDensity : IDensity {
    const int Step = 4;
    const int Lattice = (int)Chunk.Size / Step + 3; // 7 points/axis: local -4,0,4,8,12,16,20 (one spare each side)

    readonly IDensity inner;
    readonly ThreadLocal<Slab[]> cache;

    public TricubicDensity(IDensity inner) {
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

    sealed class Slab {
        public readonly double[] Values = new double[Chunk.Size * Chunk.Size * Chunk.Size];
        int cx, cy, cz;
        bool valid;

        public bool Matches(int x, int y, int z) => valid && x == cx && y == cy && z == cz;

        public void Build(IDensity inner, int cubeX, int cubeY, int cubeZ) {
            int baseX = cubeX << Chunk.Shifts, baseY = cubeY << Chunk.Shifts, baseZ = cubeZ << Chunk.Shifts;

            // 7^3 lattice; index (i,j,k) -> world offset (i-1)*Step, so index 1 is local 0 and index 6 is local 20.
            Span<double> l = stackalloc double[Lattice * Lattice * Lattice];
            for (int i = 0; i < Lattice; i++)
                for (int j = 0; j < Lattice; j++)
                    for (int k = 0; k < Lattice; k++)
                        l[(i * Lattice + j) * Lattice + k] =
                            inner.At(baseX + (i - 1) * Step, baseY + (j - 1) * Step, baseZ + (k - 1) * Step);

            const int L2 = Lattice * Lattice;
            Span<double> cyRow = stackalloc double[4];
            Span<double> czRow = stackalloc double[4];
            for (int ly = 0; ly < Chunk.Size; ly++) {
                int sy = ly / Step; double ty = (ly % Step) / (double)Step;
                for (int lz = 0; lz < Chunk.Size; lz++) {
                    int sz = lz / Step; double tz = (lz % Step) / (double)Step;
                    for (int lx = 0; lx < Chunk.Size; lx++) {
                        int sx = lx / Step; double tx = (lx % Step) / (double)Step;

                        // Collapse x (Lattice^2 stride), then y, then z, over the 4x4x4 control block at (sx,sy,sz).
                        for (int c = 0; c < 4; c++) {
                            for (int b = 0; b < 4; b++) {
                                int jk = (sy + b) * Lattice + (sz + c);
                                cyRow[b] = MathUtil.CatmullRom(l[sx * L2 + jk], l[(sx + 1) * L2 + jk],
                                                              l[(sx + 2) * L2 + jk], l[(sx + 3) * L2 + jk], tx);
                            }
                            czRow[c] = MathUtil.CatmullRom(cyRow[0], cyRow[1], cyRow[2], cyRow[3], ty);
                        }
                        Values[(int)Chunk.Index(lx, ly, lz)] = MathUtil.CatmullRom(czRow[0], czRow[1], czRow[2], czRow[3], tz);
                    }
                }
            }

            cx = cubeX; cy = cubeY; cz = cubeZ; valid = true;
        }
    }
}
