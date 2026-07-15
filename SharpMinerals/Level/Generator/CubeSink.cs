using SharpMinerals.Blocks;

namespace SharpMinerals.Level.Generator;

/// <summary>A world-coordinate block writer for a single chunk-cube that clips anything outside the cube. A
/// feature stamps its cells in absolute world coordinates and the sink silently drops the ones that fall in a
/// neighbouring cube - so a feature straddling a border is written correctly by each cube it reaches into,
/// each keeping only its own slice, with no shared state. This is the per-cube half of stitching (the driver
/// owns finding which features reach the cube); it replaces the hand-inlined bounds-check + air test the
/// decorators each carried.</summary>
public readonly struct CubeSink {
    readonly Chunk chunk;
    readonly int baseX, baseY, baseZ;

    public CubeSink(Chunk chunk, int baseX, int baseY, int baseZ) {
        this.chunk = chunk;
        this.baseX = baseX;
        this.baseY = baseY;
        this.baseZ = baseZ;
    }

    /// <summary>Writes <paramref name="block"/> at world (wx, wy, wz) only if the cell is in this cube and currently
    /// air, so existing terrain and earlier cells of the same feature (a trunk under a canopy) are never overwritten.</summary>
    public void PlaceIfAir(int wx, int wy, int wz, BlockType block) {
        int lx = wx - baseX, ly = wy - baseY, lz = wz - baseZ;
        if (lx < 0 || ly < 0 || lz < 0 || lx >= Chunk.Size || ly >= Chunk.Size || lz >= Chunk.Size) return;
        if (chunk.GetBlock(lx, ly, lz).IsAir) chunk.SetBlock(lx, ly, lz, block);
    }

    /// <summary>Writes <paramref name="block"/> at world (wx, wy, wz) if the cell is in this cube, overwriting whatever
    /// is there. Use when the feature has already established the cell is clear (ground cover on a known-open top).</summary>
    public void Place(int wx, int wy, int wz, BlockType block) {
        int lx = wx - baseX, ly = wy - baseY, lz = wz - baseZ;
        if (lx < 0 || ly < 0 || lz < 0 || lx >= Chunk.Size || ly >= Chunk.Size || lz >= Chunk.Size) return;
        chunk.SetBlock(lx, ly, lz, block);
    }
}
