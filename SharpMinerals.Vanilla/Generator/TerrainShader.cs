using SharpMinerals.Blocks;
using SharpMinerals.Level.Generator;

namespace SharpMinerals.Vanilla.Generator;

/// <summary>Terrain pass: stone where the density field is solid, air otherwise. It is the first pass, so it
/// ignores <c>current</c>. The surface pass runs after and turns the top of the stone into grass and dirt.</summary>
public sealed class TerrainShader : IChunkShader {
    readonly IDensity density;
    readonly BlockType stone = VanillaMod.Stone;
    readonly BlockType air = CoreMod.Air;

    public TerrainShader(IDensity density) => this.density = density;

    public BlockType Shade(int x, int y, int z, BlockType current) =>
        density.At(x, y, z) > 0 ? stone : air;
}
