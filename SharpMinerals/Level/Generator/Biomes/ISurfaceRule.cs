using SharpMinerals.Blocks;

namespace SharpMinerals.Level.Generator.Biomes;

/// <summary>Decides the block at a solid cell from how far below the surface it sits (0 = the topmost solid
/// cell). Covers grass-over-dirt, beaches, underwater filler, snow caps, and bare stone on steep slopes.
/// Surface blocks are discrete, so a cell takes the dominant biome's rule (optionally dithered near borders)
/// rather than a blend. P1's grass/dirt logic lives inline in the surface shader; this is the extension
/// point biomes (vanilla or core) implement in the biome phase.</summary>
public interface ISurfaceRule {
    BlockType Block(int x, int y, int z, int depthBelowSurface, BlockType current);
}
