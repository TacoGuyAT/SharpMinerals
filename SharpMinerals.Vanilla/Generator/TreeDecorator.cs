using SharpMinerals.Blocks;
using SharpMinerals.Level;
using SharpMinerals.Level.Generator;
using SharpMinerals.Level.Generator.Biomes;
using SharpMinerals.Math;

namespace SharpMinerals.Vanilla.Generator;

/// <summary>Scatters simple oak trees over the finished terrain. Placement is deterministic and stateless: each
/// column independently decides (from a seeded hash, its biome's tree density, and the feature-density map)
/// whether a tree roots there; every cube the tree reaches into stamps just its overlapping cells, so trees
/// straddle cube borders seamlessly without shared state. Only dry grass columns (surface at or above sea
/// level) in wooded biomes get trees.</summary>
public sealed class TreeDecorator : IChunkDecorator {
    const int CanopyRadius = 2;   // leaves reach this far horizontally from the trunk
    const int TreeSpacing = 6;    // one tree per TreeSpacing-grid cell (jittered)
    const int TreeJitter = 3;     // jitter within a cell; TreeSpacing - TreeJitter (=3) >= CanopyRadius + 1, so no
                                  // two trunks ever fall within a canopy -> no overlapping trees, no leaf-through-log
    const int ScanMargin = 16;    // +/- around the 2D height estimate within which to find the exact surface
    const int MaxTreeHeight = 9;  // tallest a tree gets (trunk + canopy); used for the cube y-overlap test
    const int TreeMaxSurfaceY = 140; // no land surface (hence no tree) sits above this - lets sky cubes bail fast

    readonly int seed;
    readonly BiomeSource source;
    readonly BiomeDensity heights; // cheap 2D surface estimate (scan centre)
    readonly IDensity terrain;     // the interpolated field the terrain was built from (exact surface)
    readonly BlockType log = VanillaMod.OakLog;
    readonly BlockType leaves = VanillaMod.OakLeaves;

    public TreeDecorator(int seed, BiomeSource source, BiomeDensity heights, IDensity terrain) {
        this.seed = seed;
        this.source = source;
        this.heights = heights;
        this.terrain = terrain;
    }

    public void Decorate(Chunk chunk, Vector3i cube) {
        int baseX = (int)(cube.X * Chunk.Size);
        int baseY = (int)(cube.Y * Chunk.Size);
        int baseZ = (int)(cube.Z * Chunk.Size);

        // Trees only live in a band near the surface; skip cubes wholly below sea level or up in the sky.
        if (baseY + Chunk.Size <= WorldDefaults.SeaLevel || baseY > TreeMaxSurfaceY + MaxTreeHeight) return;

        // Walk the spacing-grid cells whose (jittered) tree could reach this cube, one tree per cell.
        int reach = CanopyRadius + TreeJitter, size = (int)Chunk.Size;
        int minGX = FloorDiv(baseX - reach, TreeSpacing), maxGX = FloorDiv(baseX + size - 1 + reach, TreeSpacing);
        int minGZ = FloorDiv(baseZ - reach, TreeSpacing), maxGZ = FloorDiv(baseZ + size - 1 + reach, TreeSpacing);

        for (int gz = minGZ; gz <= maxGZ; gz++)
            for (int gx = minGX; gx <= maxGX; gx++) {
                if (!TreeInCell(gx, gz, out int ox, out int oz, out int trunkH)) continue;

                // Cheap 2D estimate first: skip trees whose possible span can't reach this cube's y-range.
                double est = heights.SurfaceHeight(ox, oz);
                if (est + ScanMargin + 1 + MaxTreeHeight < baseY) continue;
                if (est - ScanMargin + 1 >= baseY + Chunk.Size) continue;

                int surfaceTop = FindSurfaceTop(ox, oz, (int)est);
                if (surfaceTop < WorldDefaults.SeaLevel) continue; // underwater or too low -> no tree on dry grass
                if (source.IsCoastal(ox, oz, surfaceTop)) continue; // beaches are sand - no trees on the shore

                StampTree(chunk, baseX, baseY, baseZ, ox, surfaceTop + 1, oz, trunkH);
            }
    }

    // The single tree (if any) for a spacing-grid cell: a jittered column and trunk height, all from the cell's
    // hash so every cube that the tree reaches into derives the same tree.
    bool TreeInCell(int gx, int gz, out int ox, out int oz, out int trunkH) {
        ox = gx * TreeSpacing + (int)(Hash01(gx, gz, 0x10) * (TreeJitter + 1));
        oz = gz * TreeSpacing + (int)(Hash01(gx, gz, 0x20) * (TreeJitter + 1));
        trunkH = 0;

        double density = source.Dominant(ox, oz).TreeDensity;
        if (density <= 0.0) return false;
        // TreeDensity is per-column; scale to a per-cell chance (a cell covers TreeSpacing^2 columns).
        double perCell = density * TreeSpacing * TreeSpacing * source.FeatureDensity(ox, oz);
        if (Hash01(gx, gz, 0x7A) >= perCell) return false;

        trunkH = 4 + (int)(Hash01(gx, gz, 0x1B) * 3.0); // 4..6 logs
        return true;
    }

    static int FloorDiv(int a, int b) => a >= 0 ? a / b : -(((-a) + b - 1) / b);

    // Highest solid Y at the column, found by scanning the terrain density around the 2D estimate.
    int FindSurfaceTop(int x, int z, int estimate) {
        for (int y = estimate + ScanMargin; y >= estimate - ScanMargin; y--)
            if (terrain.At(x, y, z) > 0) return y;
        return int.MinValue;
    }

    void StampTree(Chunk chunk, int baseX, int baseY, int baseZ, int ox, int treeBase, int oz, int trunkH) {
        // Trunk first, so a leaf never overwrites a log where the canopy passes the trunk column.
        for (int i = 0; i < trunkH; i++)
            Put(chunk, baseX, baseY, baseZ, ox, treeBase + i, oz, log);

        int canopyBase = treeBase + trunkH - 2;
        LeafLayer(chunk, baseX, baseY, baseZ, ox, canopyBase, oz, radius: 2, cutCorners: true);
        LeafLayer(chunk, baseX, baseY, baseZ, ox, canopyBase + 1, oz, radius: 2, cutCorners: true);
        LeafLayer(chunk, baseX, baseY, baseZ, ox, canopyBase + 2, oz, radius: 1, cutCorners: false);
        LeafLayer(chunk, baseX, baseY, baseZ, ox, canopyBase + 3, oz, radius: 1, cutCorners: true);
    }

    void LeafLayer(Chunk chunk, int baseX, int baseY, int baseZ, int ox, int y, int oz, int radius, bool cutCorners) {
        for (int dz = -radius; dz <= radius; dz++)
            for (int dx = -radius; dx <= radius; dx++) {
                if (cutCorners && System.Math.Abs(dx) == radius && System.Math.Abs(dz) == radius) continue;
                Put(chunk, baseX, baseY, baseZ, ox + dx, y, oz + dz, leaves);
            }
    }

    // Writes a tree block only if the cell lies in this cube and is currently air (so trunk and terrain win).
    static void Put(Chunk chunk, int baseX, int baseY, int baseZ, int wx, int wy, int wz, BlockType block) {
        int lx = wx - baseX, ly = wy - baseY, lz = wz - baseZ;
        if (lx < 0 || ly < 0 || lz < 0 || lx >= Chunk.Size || ly >= Chunk.Size || lz >= Chunk.Size) return;
        if (chunk.GetBlock(lx, ly, lz).IsAir) chunk.SetBlock(lx, ly, lz, block);
    }

    double Hash01(int x, int z, int salt) {
        uint h = (uint)(x * 374761393) ^ (uint)(z * 668265263) ^ (uint)(seed * 2246822519) ^ (uint)(salt * 3266489917);
        h = (h ^ (h >> 13)) * 1274126177u;
        h ^= h >> 16;
        return h / 4294967296.0;
    }
}
