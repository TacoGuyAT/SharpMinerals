using SharpMinerals.Blocks.Descriptors;

namespace SharpMinerals.Blocks;

/// <summary>
/// A concrete block state: a <see cref="BlockType"/> plus a value for each of its
/// <c>StateProperties</c> (chest facing, slab type, …). Plain (stateless) blocks have no
/// <c>BlockState</c> stored — a cell is just the type id; only stateful blocks keep one in
/// the chunk's sparse state table. Values are indices into each property's value list.
/// </summary>
public sealed class BlockState {
    public BlockType Type { get; }
    readonly int[] values; // one per property, in StateProperties order; default 0

    public BlockState(BlockType type) {
        Type = type;
        values = new int[type.TryGet<StatesBlockDescriptor>(out var sp) ? sp.States.Count : 0];
    }

    /// <summary>The value index of a property (0/default if the type doesn't have it).</summary>
    public int Get(State property) =>
        Type.TryGet<StatesBlockDescriptor>(out var sp) && sp.IndexOf(property) is var i and >= 0 ? values[i] : 0;

    /// <summary>Sets a property's value by index (no-op if the type doesn't have the property); fluent.</summary>
    public BlockState Set(State property, int value) {
        if (Type.TryGet<StatesBlockDescriptor>(out var sp) && sp.IndexOf(property) is var i and >= 0)
            values[i] = value;
        return this;
    }

    /// <summary>Sets a property's value by name; fluent.</summary>
    public BlockState Set(State property, string value) => Set(property, property.IndexOf(value));

    /// <summary>A deep copy (independent value array) — for handing a block's state to an item and back.</summary>
    public BlockState Clone() {
        var copy = new BlockState(Type);
        System.Array.Copy(values, copy.values, values.Length);
        return copy;
    }

    /// <summary>
    /// The state a DROPPED ITEM of this block should carry: item-identity properties
    /// (<see cref="State.PreservedInItem"/>, e.g. colour) are kept; placement-only properties
    /// (facing, axis, …) are reset to default — so a chest facing east drops as a plain chest, but
    /// red wool stays red.
    /// </summary>
    public BlockState ForDrop() {
        var copy = Clone();
        if (Type.TryGet<StatesBlockDescriptor>(out var sp))
            for (int i = 0; i < sp.States.Count; i++)
                if (!sp.States[i].PreservedInItem)
                    copy.values[i] = 0; // reset placement-only property to its default
        return copy;
    }

    /// <summary>Value equality: same block type and identical property values (used to decide if items stack).</summary>
    public bool Matches(BlockState other) {
        if (Type != other.Type || values.Length != other.values.Length) return false;
        for (int i = 0; i < values.Length; i++)
            if (values[i] != other.values[i]) return false;
        return true;
    }
}
