using SharpMinerals.Blocks;
using SharpMinerals.Level.Generator;

namespace SharpMinerals.Vanilla.Generator;

/// <summary>Surface pass: caps the stone column with grass over a few dirt blocks. It reads the terrain pass
/// through <c>current</c> (only solid cells get a surface) and re-samples the density field upward, across the
/// cube border if needed, to learn how deep this cell is below the surface - the stateless surface rule.
/// Cells below the soil keep their stone. P1 is a single rule; the biome phase replaces this with per-biome
/// surface rules (sand, snow, beaches) hard-picked from the dominant biome.</summary>
public sealed class SurfaceShader : IChunkShader {
    const int SoilDepth = 4; // grass + (SoilDepth - 1) dirt below it

    readonly IDensity density;
    readonly BlockType grass = VanillaMod.GrassBlock;
    readonly BlockType dirt = VanillaMod.Dirt;

    public SurfaceShader(IDensity density) => this.density = density;

    public BlockType Shade(int x, int y, int z, BlockType current) {
        if (current.IsAir) return current;              // only solid cells get surfaced
        if (density.At(x, y + 1, z) <= 0) return grass;  // nothing solid directly above -> top of the column
        for (int k = 2; k <= SoilDepth; k++)
            if (density.At(x, y + k, z) <= 0) return dirt; // air within the soil depth above -> dirt
        return current;                                 // deeper than the soil -> stay stone
    }
}
