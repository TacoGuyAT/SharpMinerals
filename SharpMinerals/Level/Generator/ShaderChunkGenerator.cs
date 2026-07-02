using SharpMinerals.Blocks;
using SharpMinerals.Math;

namespace SharpMinerals.Level.Generator;

/// <summary>Drives a list of <see cref="IChunkShader"/> passes over every cell of a 16x16x16 cube - the
/// shader dispatcher. It owns the per-cell loop and the world-coordinate math; each cell starts as air and
/// is threaded through the passes in order, the final block written to the chunk via
/// <see cref="ChunkBuilder"/>. Stateless and deterministic: a chunk is a pure function of its world
/// coordinates, so cubes generate independently and never seam.</summary>
public sealed class ShaderChunkGenerator : IChunkGenerator {
    readonly IChunkShader[] shaders;
    readonly IChunkDecorator[] decorators;

    public ShaderChunkGenerator(params IChunkShader[] shaders) : this(shaders, []) { }

    public ShaderChunkGenerator(IChunkShader[] shaders, IChunkDecorator[] decorators) {
        this.shaders = shaders;
        this.decorators = decorators;
    }

    public Chunk Generate(Vector3i position) {
        var chunk = new Chunk(position);
        var framebuffer = new ChunkBuilder(chunk);

        int baseX = (int)(position.X * Chunk.Size);
        int baseY = (int)(position.Y * Chunk.Size);
        int baseZ = (int)(position.Z * Chunk.Size);
        var air = CoreMod.Air;

        for (int y = 0; y < Chunk.Size; y++)
            for (int z = 0; z < Chunk.Size; z++)
                for (int x = 0; x < Chunk.Size; x++) {
                    var block = air;
                    for (int s = 0; s < shaders.Length; s++)
                        block = shaders[s].Shade(baseX + x, baseY + y, baseZ + z, block);
                    framebuffer.Set(x, y, z, block);
                }

        // Stamp multi-cell features (trees, ...) over the finished terrain.
        for (int d = 0; d < decorators.Length; d++)
            decorators[d].Decorate(chunk, position);

        return chunk;
    }
}
