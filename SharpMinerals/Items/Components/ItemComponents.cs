using SharpMinerals.Blocks;

namespace SharpMinerals.Items.Components;

public sealed class Stackable {
    public int MaxStackSize;
    public Stackable(int maxStackSize) => MaxStackSize = maxStackSize;
}

public sealed class Placeable {
    public BlockType Block;
    public Placeable(BlockType block) => Block = block;
}
