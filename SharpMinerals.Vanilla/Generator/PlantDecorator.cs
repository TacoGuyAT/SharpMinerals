using SharpMinerals.Blocks;
using SharpMinerals.Level;
using SharpMinerals.Level.Generator;
using SharpMinerals.Level.Generator.Biomes;
using SharpMinerals.Math;

namespace SharpMinerals.Vanilla.Generator;

/// <summary>Scatters ground cover - short grass and flowers on exposed grass, dead bushes on exposed sand /
/// red sand. It runs after the tree decorator and reads the finished cube directly: a plant goes one block above
/// the surface only where the cell above is open, so it never lands on water, under a tree's canopy, or on the
/// wrong surface. Each plant is one cell, so it never crosses a cube border - only this cube's own columns are
/// considered. Placement is a per-column hash scaled by the biome's densities (flowers and dead bushes also fold
/// in the feature-density map, forming patches).</summary>
public sealed class PlantDecorator : IChunkDecorator {
    const int MaxSurfaceY = 140; // ground cover only sits in the surface band

    readonly int seed;
    readonly BiomeSource source;
    readonly BlockType grassBlock = VanillaMod.GrassBlock;
    readonly BlockType redSand = VanillaMod.RedSand;
    readonly BlockType sand = VanillaMod.Sand;
    readonly BlockType shortGrass = VanillaMod.ShortGrass;
    readonly BlockType deadBush = VanillaMod.DeadBush;
    readonly BlockType[] flowers = {
        VanillaMod.Dandelion, VanillaMod.Poppy, VanillaMod.Cornflower, VanillaMod.OxeyeDaisy,
    };

    public PlantDecorator(int seed, BiomeSource source) {
        this.seed = seed;
        this.source = source;
    }

    public void Decorate(Chunk chunk, Vector3i cube) {
        int baseX = (int)(cube.X * Chunk.Size);
        int baseY = (int)(cube.Y * Chunk.Size);
        int baseZ = (int)(cube.Z * Chunk.Size);
        if (baseY + Chunk.Size <= WorldDefaults.SeaLevel || baseY > MaxSurfaceY) return;

        int top = (int)Chunk.Size - 1;
        for (int lz = 0; lz < Chunk.Size; lz++)
            for (int lx = 0; lx < Chunk.Size; lx++) {
                // Highest solid cell in this cube's column; need room for the plant just above it.
                int gy = -1;
                for (int ly = top; ly >= 0; ly--)
                    if (!chunk.GetBlock(lx, ly, lz).IsAir) { gy = ly; break; }
                if (gy < 0 || gy >= top) continue; // empty column, or surface at the cube's ceiling (no room above)

                var surface = chunk.GetBlock(lx, gy, lz);
                int wx = baseX + lx, wz = baseZ + lz;
                var biome = source.Dominant(wx, wz);

                if (surface == grassBlock) {
                    if (biome.FlowerDensity > 0.0 && Hash01(wx, wz, 0xF1) < biome.FlowerDensity * source.FeatureDensity(wx, wz))
                        chunk.SetBlock(lx, gy + 1, lz, flowers[(int)(Hash01(wx, wz, 0xF2) * flowers.Length)]);
                    else if (biome.GrassDensity > 0.0 && Hash01(wx, wz, 0x67) < biome.GrassDensity)
                        chunk.SetBlock(lx, gy + 1, lz, shortGrass);
                } else if (surface == redSand || surface == sand) {
                    if (biome.DeadBushDensity > 0.0 && Hash01(wx, wz, 0xDB) < biome.DeadBushDensity * source.FeatureDensity(wx, wz))
                        chunk.SetBlock(lx, gy + 1, lz, deadBush);
                }
            }
    }

    double Hash01(int x, int z, int salt) {
        uint h = (uint)(x * 374761393) ^ (uint)(z * 668265263) ^ (uint)(seed * 2246822519) ^ (uint)(salt * 3266489917);
        h = (h ^ (h >> 13)) * 1274126177u;
        h ^= h >> 16;
        return h / 4294967296.0;
    }
}
