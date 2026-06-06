using SharpMinerals.Blocks;

namespace SharpMinerals.Level.Generator.Biomes;

/// <summary>The common surface shape: a top block, a few filler blocks beneath it, an optional base block band
/// below that, then whatever was already there (stone). Parameterised so every land/ocean biome reuses one rule
/// - grass over dirt, sand over sand, red sand over red sandstone, and so on. A submerged column (its top cell
/// has water directly above) swaps the top for <c>SubmergedTop</c>, so grass becomes dirt underwater while sandy
/// floors stay sand.</summary>
public sealed class LayeredSurfaceRule : ISurfaceRule {
    // A surface at Y is underwater when the cell above (Y+1) is water, i.e. Y+1 < SeaLevel, i.e. Y <= SeaLevel-2.
    const int WaterlineTop = WorldDefaults.SeaLevel - 2;

    readonly BlockType top;
    readonly BlockType submergedTop;
    readonly BlockType filler;
    readonly int fillerDepth;
    readonly BlockType? baseBlock; // a band below the filler (e.g. red sandstone under badlands' red sand)
    readonly int baseDepth;

    public LayeredSurfaceRule(BlockType top, BlockType filler, int fillerDepth = 3, BlockType? submergedTop = null,
                              BlockType? baseBlock = null, int baseDepth = 0) {
        this.top = top;
        this.filler = filler;
        this.fillerDepth = fillerDepth;
        this.submergedTop = submergedTop ?? top;
        this.baseBlock = baseBlock;
        this.baseDepth = baseDepth;
    }

    /// <summary>The deepest surface layer (0 = topmost). Cells deeper than this keep their stone; the surface
    /// shader's upward probe must reach at least this far to tell surface from stone.</summary>
    public int Depth => fillerDepth + (baseBlock is null ? 0 : baseDepth);

    public BlockType Block(int x, int y, int z, int depthBelowSurface, BlockType current) {
        if (depthBelowSurface == 0) return y <= WaterlineTop ? submergedTop : top;
        if (depthBelowSurface <= fillerDepth) return filler;
        if (baseBlock is { } b && depthBelowSurface <= fillerDepth + baseDepth) return b;
        return current;
    }
}
