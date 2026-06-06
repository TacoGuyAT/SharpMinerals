using SharpMinerals.Blocks;

namespace SharpMinerals.Level.Generator;

/// <summary>A zero-alloc write view over a chunk's dense block array - the "framebuffer" the
/// <see cref="ShaderChunkGenerator"/> writes shader outputs into. It wraps the chunk's own storage (no second
/// buffer) and bypasses the per-write dirty flag, since a freshly generated chunk is the persisted baseline
/// (the loader clears dirty afterwards). Coordinates are chunk-local (0..15).</summary>
public readonly ref struct ChunkBuilder {
    readonly Span<ushort> states;

    public ChunkBuilder(Chunk chunk) => states = chunk.RawStates;

    public void Set(int x, int y, int z, BlockType block) =>
        states[(int)Chunk.Index(x, y, z)] = (ushort)block.BlockId;

    public void Fill(BlockType block) => states.Fill((ushort)block.BlockId);
}
