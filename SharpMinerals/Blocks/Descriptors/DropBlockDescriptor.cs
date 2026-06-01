using SharpMinerals.Items;

namespace SharpMinerals.Blocks.Descriptors;

/// <summary>
/// The item stack a block yields when broken. Holds a lazy supplier so a block can drop a
/// kind registered later (forward reference) without a two-phase init, and so the stack —
/// with its count and per-instance <c>Data</c> — is produced fresh on each break.
/// </summary>
public sealed class DropBlockDescriptor {
    readonly Func<ItemStack> stack;
    public DropBlockDescriptor(Func<ItemStack> stack) => this.stack = stack;
    public ItemStack Stack => stack();
}
