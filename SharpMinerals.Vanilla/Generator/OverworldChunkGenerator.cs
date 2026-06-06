using SharpMinerals.Level;
using SharpMinerals.Level.Generator;
using SharpMinerals.Level.Generator.Biomes;
using SharpMinerals.Math;

namespace SharpMinerals.Vanilla.Generator;

/// <summary>The overworld generator: the registered biomes drive a shared <see cref="BiomeSource"/>, a composite
/// <see cref="BiomeDensity"/> shapes terrain, and a stone terrain pass + a per-biome surface pass are dispatched
/// per cell by <see cref="ShaderChunkGenerator"/>. Also exposes the biome at a column (<see cref="IBiomeLookup"/>).
/// Lives in the vanilla mod because it builds vanilla content (core's default stays <see cref="VoidChunkGenerator"/>).</summary>
public sealed class OverworldChunkGenerator : IChunkGenerator, IBiomeLookup {
    readonly ShaderChunkGenerator generator;
    readonly BiomeSource source;

    public OverworldChunkGenerator(int seed) {
        source = new BiomeSource(seed, BiomeRegistry.Build(seed));
        var biomeDensity = new BiomeDensity(seed, source);
        // General terrain sampling (smooth, so trilinear is fine and fast). Rivers are carved separately below.
        //IDensity terrain = biomeDensity;                          // full precision (per cell)
        //IDensity terrain = new TricubicDensity(biomeDensity);     // tricubic
        IDensity terrain = new TrilinearDensity(biomeDensity);      // trilinear x4 (fast)
        // Rivers carved per-cell (full resolution) on top, so their sharp banks stay crisp (the trilinear lattice
        // would alias them). The river carve is cheap (2D noise + the natural surface), terrain stays interpolated.
        IDensity density = new RiverDensity(terrain, source, biomeDensity);
        var shaders = new IChunkShader[] {
            new TerrainShader(density), new SurfaceShader(density, source), new WaterShader(),
        };
        var decorators = new IChunkDecorator[] {
            new TreeDecorator(seed, source, biomeDensity, density),
            new PlantDecorator(seed, source), // after trees, so it skips columns capped by trunks/canopy
        };
        generator = new ShaderChunkGenerator(shaders, decorators);
    }

    public Chunk Generate(Vector3i position) => generator.Generate(position);

    public string BiomeNameAt(int x, int z) => source.Dominant(x, z).Name;

    public static IChunkGenerator Create(int seed) => new OverworldChunkGenerator(seed);
}
