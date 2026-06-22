using SharpMinerals.Blocks;
using SharpMinerals.Level.Generator;
using SharpMinerals.Level.Generator.Biomes;

namespace SharpMinerals.Vanilla.Generator;

/// <summary>Surface pass: caps each solid column with its biome's surface (grass/dirt, red sand, sea floor, ...).
/// It reads the terrain pass through <c>current</c> (only solid cells are surfaced), measures how deep the cell
/// is below the surface by re-sampling the density field upward - across the cube border if needed, the
/// stateless surface rule - then asks the dithered surface biome at this column which block to place. Cells
/// below the soil keep their stone.</summary>
public sealed class SurfaceShader : IChunkShader {
    readonly IDensity density;
    readonly BiomeSource source;
    readonly int maxProbe; // one past the deepest surface layer of any biome - so deeper cells read as stone

    public SurfaceShader(IDensity density, BiomeSource source) {
        this.density = density;
        this.source = source;
        int deepest = 0;
        foreach (var biome in source.Biomes) deepest = System.Math.Max(deepest, biome.Surface.Depth);
        if (source.CoastalBiome is { } coastal) deepest = System.Math.Max(deepest, coastal.Surface.Depth);
        maxProbe = deepest + 1;
    }

    public BlockType Shade(int x, int y, int z, BlockType current) {
        if (current.IsAir) return current; // only solid cells get surfaced

        // Rocky columns strip their soil cap to bare stone (alpha/beta stone cliffs/shores); current is already stone.
        if (source.StripSoil(x, z)) return current;

        int depth = 0; // solid cells stacked directly above -> depth below the surface (0 = topmost)
        for (int k = 1; k <= maxProbe; k++) {
            if (density.At(x, y + k, z) > 0) depth++;
            else break;
        }

        // y + depth is the column's top solid cell - the surface height the coastal (beach) rule is keyed to.
        return source.SurfaceBiomeAt(x, z, y + depth).Surface.Block(x, y, z, depth, current);
    }
}
