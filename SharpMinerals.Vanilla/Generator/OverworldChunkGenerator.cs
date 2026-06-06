using SharpMinerals.Level;
using SharpMinerals.Level.Generator;

namespace SharpMinerals.Vanilla.Generator;

/// <summary>Builds the P1 overworld generator: a seeded continentalness/FBm density driving a stone terrain
/// pass and a grass/dirt surface pass, dispatched per cell by <see cref="ShaderChunkGenerator"/>. A single
/// biome for now; the biome phase introduces the biome source and per-biome shaping. Lives in the vanilla
/// mod because it builds vanilla blocks (core's default stays <see cref="VoidChunkGenerator"/>).</summary>
public static class OverworldChunkGenerator {
    public static IChunkGenerator Create(int seed) {
        var density = new OverworldDensity(seed);
        return new ShaderChunkGenerator(new TerrainShader(density), new SurfaceShader(density));
    }
}
