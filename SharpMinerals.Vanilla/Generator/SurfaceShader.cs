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
    const int MaxProbe = 5; // deepest filler any biome rule uses, plus one - bounds the upward scan

    readonly IDensity density;
    readonly BiomeSource source;

    public SurfaceShader(IDensity density, BiomeSource source) {
        this.density = density;
        this.source = source;
    }

    public BlockType Shade(int x, int y, int z, BlockType current) {
        if (current.IsAir) return current; // only solid cells get surfaced

        int depth = 0; // solid cells stacked directly above -> depth below the surface (0 = topmost)
        for (int k = 1; k <= MaxProbe; k++) {
            if (density.At(x, y + k, z) > 0) depth++;
            else break;
        }

        return source.SurfacePick(x, z).Surface.Block(x, y, z, depth, current);
    }
}
