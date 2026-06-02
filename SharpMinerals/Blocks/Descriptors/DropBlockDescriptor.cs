using SharpMinerals.Items;

namespace SharpMinerals.Blocks.Descriptors;

/// <summary>The item stack a block yields when broken. A lazy supplier, so a block can drop a kind
/// registered later and the stack is produced fresh on each break.</summary>
public sealed class DropBlockDescriptor {
    readonly Func<ItemStack> stack;
    public DropBlockDescriptor(Func<ItemStack> stack) => this.stack = stack;
    public ItemStack Stack => stack();
}
