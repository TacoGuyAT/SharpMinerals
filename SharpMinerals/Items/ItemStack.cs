using SharpMinerals.Blocks;
using SharpMinerals.Components;

namespace SharpMinerals.Items;

/// <summary>
/// A quantity of an item: a reference to its <see cref="ItemType"/> definition, an
/// optional per-instance <see cref="Data"/> bag (NBT-like — colour, custom name, …),
/// and a count. A plain value type, not an ECS component itself; it lives inside an
/// <c>Inventory</c> or a <c>DroppedItem</c>. An empty stack has a null type or zero count.
/// </summary>
/// <remarks>
/// Copying an <see cref="ItemStack"/> shares the same <see cref="Data"/> reference, so a
/// stack that carries instance data (e.g. a coloured wool's <c>BlockState</c>) must be
/// cloned before independent mutation. Plain items leave <see cref="Data"/> null.
/// </remarks>
public struct ItemStack {
    public ItemType? Type;
    public ComponentObject? Data;
    public int Count;

    public ItemStack(ItemType type, int count = 1) {
        Type = type;
        Count = count;
    }

    public readonly bool IsEmpty => Type == null || Count <= 0;

    /// <summary>The block state this stack carries (e.g. a wool's colour), or null if plain.</summary>
    public readonly BlockState? State =>
        Data is { } d && d.TryGet<BlockState>(out var s) ? s : null;

    /// <summary>Attaches a carried block state (the item's variant), replacing any data; fluent.</summary>
    public ItemStack WithState(BlockState state) {
        Data = new ComponentObject().Add(state);
        return this;
    }

    /// <summary>
    /// Whether two stacks are the same item AND carry the same state, so their counts may
    /// merge. Differently-coloured wools (different carried <see cref="State"/>) do not stack.
    /// </summary>
    public readonly bool StacksWith(ItemStack other) {
        if (Type != other.Type) return false;
        var a = State;
        var b = other.State;
        return a is null ? b is null : b is not null && a.Matches(b);
    }

    public override readonly string ToString() => IsEmpty ? "empty" : $"{Count}x {Type!.Name}";
}
