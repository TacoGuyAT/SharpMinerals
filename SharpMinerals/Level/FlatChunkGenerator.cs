using SharpMinerals.Blocks;
using SharpMinerals.Math;

namespace SharpMinerals.Level;

/// <summary>
/// A classic superflat generator: bedrock at world y=0, three layers of dirt, and
/// a grass surface, with air everywhere above and below. The surface sits at
/// <see cref="SurfaceY"/> (the top face of the grass block).
/// </summary>
public sealed class FlatChunkGenerator : IChunkGenerator {
    /// <summary>World Y of the topmost solid layer (grass). Entities stand at SurfaceY+1.</summary>
    public const int GrassY = 4;
    public const int SurfaceY = GrassY + 1;

    public Chunk Generate(Vector3i position) {
        var chunk = new Chunk(position);

        for (int y = 0; y < Chunk.Size; y++) {
            long worldY = position.Y * Chunk.Size + y;
            var block = LayerAt(worldY);
            if (block.IsAir)
                continue; // leave the default air in place

            for (int x = 0; x < Chunk.Size; x++)
                for (int z = 0; z < Chunk.Size; z++)
                    chunk.SetBlock(x, y, z, block);
        }

        return chunk;
    }

    static BlockType LayerAt(long worldY) => worldY switch {
        0 => BlockRegistry.Bedrock,
        >= 1 and <= 3 => BlockRegistry.Dirt,
        GrassY => BlockRegistry.GrassBlock,
        _ => BlockRegistry.Air,
    };
}
