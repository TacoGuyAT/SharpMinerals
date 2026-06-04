using SharpMinerals.Blocks;
using SharpMinerals.Level;
using SharpMinerals.Math;

namespace SharpMinerals.Vanilla;

/// <summary>A classic superflat generator: bedrock, three layers of dirt, and a grass surface, air elsewhere. The
/// surface sits at <see cref="WorldDefaults.SurfaceY"/>. Lives in the vanilla mod because it builds vanilla blocks;
/// the host injects it (core's default is <see cref="VoidChunkGenerator"/>).</summary>
public sealed class FlatChunkGenerator : IChunkGenerator {
    public Chunk Generate(Vector3i position) {
        var chunk = new Chunk(position);

        for (int y = 0; y < Chunk.Size; y++) {
            long worldY = position.Y * Chunk.Size + y;
            var block = LayerAt(worldY);
            if (block.IsAir)
                continue;

            for (int x = 0; x < Chunk.Size; x++)
                for (int z = 0; z < Chunk.Size; z++)
                    chunk.SetBlock(x, y, z, block);
        }

        return chunk;
    }

    static BlockType LayerAt(long worldY) => worldY switch {
        0 => VanillaMod.Bedrock,
        >= 1 and <= 3 => VanillaMod.Dirt,
        WorldDefaults.GrassY => VanillaMod.GrassBlock,
        _ => BlockRegistry.Air,
    };
}
