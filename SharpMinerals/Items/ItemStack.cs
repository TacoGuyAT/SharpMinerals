using SharpMinerals.Components;

namespace SharpMinerals.Items;

/// <summary>
/// A quantity of an item: a reference to its <see cref="ItemType"/> definition, an
/// optional per-instance <see cref="Data"/> bag (NBT-like — colour, custom name, …),
/// and a count. A plain value type, not an ECS component itself; it lives inside an
/// <c>Inventory</c> or a <c>DroppedItem</c>. An empty stack has a null type or zero count.
/// </summary>
/// <remarks>
/// Copying an <see cref="ItemStack"/> shares the same <see cref="Data"/> reference.
/// <see cref="Data"/> is always null in the current server; once stacks carry instance
/// data, splitting a stack must clone it.
/// </remarks>
public struct ItemStack {
    public ItemType? Type;
    public Composed? Data;
    public int Count;

    public ItemStack(ItemType type, int count = 1) {
        Type = type;
        Data = null;
        Count = count;
    }

    public readonly bool IsEmpty => Type is null || Count <= 0;

    public override readonly string ToString() => IsEmpty ? "empty" : $"{Count}x {Type!.Name}";
}
