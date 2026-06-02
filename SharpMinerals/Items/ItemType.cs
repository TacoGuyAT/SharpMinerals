using SharpMinerals.Blocks;
using SharpMinerals.Components;
using SharpMinerals.Items.Components;

namespace SharpMinerals.Items;

/// <summary>A registered item type — a flyweight definition (one shared instance per kind) assembled from
/// components. Everything beyond identity (stack size, placement, …) lives in components.
/// <see cref="BlockType"/> derives from this — every block is also an item.</summary>
public class ItemType : ComponentObject {
    internal int ItemId { get; }
    public string Name { get; }

    internal ItemType(int id, string name) {
        ItemId = id;
        Name = name;
    }

    /// <summary>Max stack size (from a <see cref="Stackable"/> component, default 64).</summary>
    public int MaxStackSize => TryGet<Stackable>(out var s) ? s.MaxStackSize : 64;

    /// <summary>The block this item places when used, if any (from a <see cref="Placeable"/> component).</summary>
    public virtual BlockType? PlacedBlock => TryGet<Placeable>(out var p) ? p.Block : null;

    public override string ToString() => Name;
}
