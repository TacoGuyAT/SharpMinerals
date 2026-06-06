using SharpMinerals.Blocks;
using SharpMinerals.Level;
using SharpMinerals.Level.Generator;

namespace SharpMinerals.Vanilla.Generator;

/// <summary>Floods every air cell below sea level with water, turning the ocean basins (and any low
/// depressions) into seas and lakes. It runs after the terrain and surface passes, so it only fills empty
/// space above the already-surfaced sea floor and never replaces solid ground.</summary>
public sealed class WaterShader : IChunkShader {
    readonly BlockType water = VanillaMod.Water;

    public BlockType Shade(int x, int y, int z, BlockType current) =>
        current.IsAir && y < WorldDefaults.SeaLevel ? water : current;
}
