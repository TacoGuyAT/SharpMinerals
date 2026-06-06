using SharpMinerals.Blocks;

namespace SharpMinerals.Level.Generator;

/// <summary>One generation pass applied per cell, like a fragment shader: given a world coordinate and the
/// block the previous pass produced (air for the first pass), return the block for this cell - return
/// <paramref name="current"/> to leave it unchanged. The framework (<see cref="ShaderChunkGenerator"/>) owns
/// the per-cell loop and writes the result into the chunk; a pass holds its resolved blocks and noise
/// samplers as fields (its "uniforms"). Passes must stay pure functions of their inputs so cubes generate
/// identically regardless of order.</summary>
public interface IChunkShader {
    BlockType Shade(int x, int y, int z, BlockType current);
}
