using SharpMinerals.Blocks;

namespace SharpMinerals.Items.Components;

/// <summary>How many of an item fit in one stack.</summary>
public sealed class Stackable {
    public int MaxStackSize;
    public Stackable(int maxStackSize) => MaxStackSize = maxStackSize;
}

/// <summary>Marks an item that places a block when used; carries the block it places.</summary>
public sealed class Placeable {
    public BlockType Block;
    public Placeable(BlockType block) => Block = block;
}
