using SharpMinerals.Level.Generator.Biomes;

namespace SharpMinerals.Level.Generator.Features;

/// <summary>Memoises the terrain-surface resolution a scatter feature needs at a column - the cheap 2D height
/// estimate and the exact highest-solid <c>Y</c> found by scanning the density field - so it is computed once per
/// column instead of once per cube that reaches it. A scattered feature is resolved by every cube its footprint
/// overlaps (its whole vertical stack of surface-band cubes re-walks the same x,z grid, and border cubes re-walk
/// the shared columns), and by every scatter feature bound to the world; all of that collapses to one estimate
/// and one scan here. Sound because the estimate (climate + biome heights) and the scan (the pure density field,
/// never the mutable chunk) depend only on (x, z).
///
/// Per-thread and memo-only, exactly like <see cref="BiomeSource"/>'s climate cache: a miss (empty or collided
/// slot) just recomputes the seeded value, so a collision is never wrong - only a touch slower - and nothing
/// depends on call order. Lives on <see cref="FeatureWorld"/> so every scatter placement shares one cache.</summary>
public sealed class TerrainSurfaceCache {
    /// <summary>Half-height of the vertical scan window around the 2D estimate, in blocks.</summary>
    public const int ScanMargin = 16;

    const byte Estimated = 1; // the estimate is cached in this slot
    const byte Resolved = 2;  // the scan has run for this slot
    const byte Found = 4;     // ... and it found a surface

    const int Bits = 12; // 4096 slots ~= 16 chunk column footprints, plenty for a cube's decoration working set
    const int Size = 1 << Bits;

    readonly BiomeDensity heights;
    readonly IDensity density;
    readonly ThreadLocal<Entry[]> slots = new(() => new Entry[Size]);

    public TerrainSurfaceCache(BiomeDensity heights, IDensity density) {
        this.heights = heights;
        this.density = density;
    }

    /// <summary>The cheap 2D surface estimate at a column (the scan centre; also drives the cube-reach cull).</summary>
    public double Estimate(int x, int z) {
        long key = Key(x, z);
        ref var e = ref slots.Value![Slot(key)];
        // Same column already estimated: return it WITHOUT touching the resolved-scan bits, so a re-estimate from
        // a later cube keeps this column's cached scan (the cross-cube win).
        if (e.Key == key && (e.Flags & Estimated) != 0) return e.Est;
        e.Key = key;
        e.Est = heights.SurfaceHeight(x, z);
        e.Flags = Estimated; // new column in this slot: scan is now unresolved
        return e.Est;
    }

    /// <summary>The highest solid cell at a column, scanning a <see cref="ScanMargin"/> window around the estimate.
    /// Returns false (leaving <paramref name="surfaceY"/> at <see cref="int.MinValue"/>) when none is in range.</summary>
    public bool TryResolve(int x, int z, out int surfaceY) {
        long key = Key(x, z);
        ref var e = ref slots.Value![Slot(key)];
        if (e.Key == key && (e.Flags & Resolved) != 0) {
            surfaceY = e.SurfaceY;
            return (e.Flags & Found) != 0;
        }

        int estimate = (int)(e.Key == key && (e.Flags & Estimated) != 0 ? e.Est : Estimate(x, z));
        bool found = Scan(x, z, estimate, out surfaceY);

        // The slot still holds this column (Estimate ran on it just above, or it was already estimated), so augment
        // it in place; if it had been repurposed we would have missed the key check and re-estimated it here.
        ref var slot = ref slots.Value![Slot(key)];
        slot.Key = key;
        slot.SurfaceY = surfaceY;
        slot.Flags |= (byte)(Resolved | (found ? Found : 0));
        return found;
    }

    bool Scan(int x, int z, int estimate, out int surfaceY) {
        for (int y = estimate + ScanMargin; y >= estimate - ScanMargin; y--)
            if (density.At(x, y, z) > 0) {
                surfaceY = y;
                return true;
            }
        surfaceY = int.MinValue;
        return false;
    }

    static long Key(int x, int z) => ((long)(uint)x << 32) | (uint)z;

    // Fibonacci hash of the whole 64-bit key, so both packed coordinates mix into the top Bits bits.
    static int Slot(long key) => (int)((ulong)key * 0x9E3779B97F4A7C15UL >> (64 - Bits));

    struct Entry {
        public long Key;
        public double Est;
        public int SurfaceY;
        public byte Flags;
    }
}
