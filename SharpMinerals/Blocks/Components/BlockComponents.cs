using SharpMinerals.Items;

namespace SharpMinerals.Blocks.Components;

/// <summary>The item a block yields when broken.</summary>
public sealed class Drops {
    public ItemType Item;
    public Drops(ItemType item) => Item = item;
}

/// <summary>Marker: this block is air (also cached as a core bool on <see cref="BlockType"/> for the hot path).</summary>
public sealed class Air { }

/// <summary>Marks a block that opens a container window when used; carries its slot count.</summary>
public sealed class Container {
    public int Size;
    public Container(int size) => Size = size;
}
