namespace SharpMinerals.Level.Generator;

/// <summary>A per-thread cache of the two most-recently-touched chunk-cubes' per-column state (a 16x16 grid of
/// <typeparamref name="TCol"/>), so a density that derives 2D per-column data once and reuses it down the vertical
/// stack never rebuilds per cell. This is the shared machinery behind the trilinear lattice and the carves, which
/// all built the same LRU by hand.
///
/// An optional cube predicate skips whole cubes cheaply: when <c>cubeActive</c> returns false the cube is marked
/// empty and <em>no</em> column is built, so a rare feature (whose low-frequency gate is clearly absent across the
/// cube) costs a handful of samples instead of 256. Deterministic and thread-safe: it only memoises pure samples.</summary>
public sealed class ColumnCache<TCol> where TCol : struct {
    readonly Func<int, int, TCol> buildColumn; // (worldX, worldZ) -> column state
    readonly Func<int, int, bool>? cubeActive;  // (cubeX, cubeZ) -> can this cube hold content? null = always
    readonly ThreadLocal<Slab[]> cache;

    public ColumnCache(Func<int, int, TCol> buildColumn, Func<int, int, bool>? cubeActive = null) {
        this.buildColumn = buildColumn;
        this.cubeActive = cubeActive;
        cache = new ThreadLocal<Slab[]>(() => [new Slab(), new Slab()]);
    }

    /// <summary>The cached column state at world column (x, z). Returns false (and leaves <paramref name="col"/>
    /// defaulted) when the whole cube was skipped, i.e. holds no content.</summary>
    public bool TryColumn(int x, int z, out TCol col) {
        int cx = x >> Chunk.Shifts, cz = z >> Chunk.Shifts;
        var slabs = cache.Value!;

        Slab slab;
        if (slabs[0].Matches(cx, cz)) {
            slab = slabs[0];
        } else if (slabs[1].Matches(cx, cz)) {
            (slabs[0], slabs[1]) = (slabs[1], slabs[0]); // promote to most-recent
            slab = slabs[0];
        } else {
            var lru = slabs[1];
            lru.Build(buildColumn, cubeActive, cx, cz);
            (slabs[0], slabs[1]) = (lru, slabs[0]);
            slab = slabs[0];
        }

        if (slab.Empty) {
            col = default;
            return false;
        }
        col = slab.Cols[(int)(x & Chunk.Mask) * (int)Chunk.Size + (int)(z & Chunk.Mask)];
        return true;
    }

    /// <summary>One built cube's per-column state plus its coordinate, or an "empty" flag for a skipped cube.</summary>
    sealed class Slab {
        public readonly TCol[] Cols = new TCol[Chunk.Size * Chunk.Size];
        public bool Empty;
        int cx, cz;
        bool valid;

        public bool Matches(int x, int z) => valid && x == cx && z == cz;

        public void Build(Func<int, int, TCol> build, Func<int, int, bool>? active, int cubeX, int cubeZ) {
            cx = cubeX; cz = cubeZ; valid = true;
            if (active is not null && !active(cubeX, cubeZ)) { Empty = true; return; }
            Empty = false;

            int baseX = cubeX << Chunk.Shifts, baseZ = cubeZ << Chunk.Shifts, size = (int)Chunk.Size;
            for (int lx = 0; lx < size; lx++)
                for (int lz = 0; lz < size; lz++)
                    Cols[lx * size + lz] = build(baseX + lx, baseZ + lz);
        }
    }
}
