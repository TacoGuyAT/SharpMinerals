using SharpMinerals.Blocks;

namespace SharpMinerals.Level.Generator.Biomes;

/// <summary>The common surface shape: a top block, a few filler blocks beneath it, then whatever was already
/// there (stone). Parameterised so every land/ocean biome reuses one rule - grass over dirt, sand over sand,
/// sea floor over dirt, and so on. A submerged column (its top cell has water directly above) swaps the top
/// for <c>SubmergedTop</c>, so grass becomes dirt underwater while sandy floors stay sand.</summary>
public sealed class LayeredSurfaceRule : ISurfaceRule {
    // A surface at Y is underwater when the cell above (Y+1) is water, i.e. Y+1 < SeaLevel, i.e. Y <= SeaLevel-2.
    const int WaterlineTop = WorldDefaults.SeaLevel - 2;

    readonly BlockType top;
    readonly BlockType submergedTop;
    readonly BlockType filler;
    readonly int fillerDepth;

    public LayeredSurfaceRule(BlockType top, BlockType filler, int fillerDepth = 3, BlockType? submergedTop = null) {
        this.top = top;
        this.filler = filler;
        this.fillerDepth = fillerDepth;
        this.submergedTop = submergedTop ?? top;
    }

    public BlockType Block(int x, int y, int z, int depthBelowSurface, BlockType current) =>
        depthBelowSurface == 0 ? (y <= WaterlineTop ? submergedTop : top)
        : depthBelowSurface <= fillerDepth ? filler
        : current;
}
