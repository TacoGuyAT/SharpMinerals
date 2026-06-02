using SharpMinerals.Blocks;
using SharpMinerals.Components;

namespace SharpMinerals.Items;

/// <summary>A quantity of an item: an <see cref="ItemType"/> reference, an optional per-instance
/// <see cref="Data"/> bag (NBT-like), and a count. A value type. Empty when the type is null or count ≤ 0.</summary>
/// <remarks>Copying shares the same <see cref="Data"/> reference, so a stack carrying instance data must be
/// cloned before independent mutation.</remarks>
public struct ItemStack {
    public ItemType? Type;
    public ComponentObject? Data;
    public int Count;

    public ItemStack(ItemType type, int count = 1) {
        Type = type;
        Count = count;
    }

    public readonly bool IsEmpty => Type == null || Count <= 0;

    public readonly BlockState? State =>
        Data is { } d && d.TryGet<BlockState>(out var s) ? s : null;

    public ItemStack WithState(BlockState state) {
        Data = new ComponentObject().Add(state);
        return this;
    }

    public readonly bool StacksWith(ItemStack other) {
        if (Type != other.Type) return false;
        var a = State;
        var b = other.State;
        return a is null ? b is null : b is not null && a.Matches(b);
    }

    public override readonly string ToString() => IsEmpty ? "empty" : $"{Count}x {Type!.Name}";
}
