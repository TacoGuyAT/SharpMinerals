using SharpMinerals.Blocks;
using SharpMinerals.Components;
using SharpMinerals.Items.Components;

namespace SharpMinerals.Items;

/// <summary>
/// A registered item type — a flyweight definition (one shared instance per kind),
/// composed of components. Identity (<see cref="Id"/>, <see cref="Name"/>) is kept as
/// core fields; everything else (stack size, vanilla mapping, placement) lives in
/// components, so new kinds are assembled by composition. <see cref="BlockType"/>
/// derives from this — every block is also an item.
/// </summary>
public class ItemType : Composed {
    public int Id { get; }
    public string Name { get; }

    internal ItemType(int id, string name) {
        Id = id;
        Name = name;
    }

    /// <summary>Max stack size (from <see cref="Stackable"/>, default 64).</summary>
    public int MaxStackSize => TryGet<Stackable>(out var s) ? s.MaxStackSize : 64;

    /// <summary>The block this item places when used, if any (from <see cref="Placeable"/>).</summary>
    public BlockType? PlacedBlock => TryGet<Placeable>(out var p) ? p.Block : null;

    public override string ToString() => Name;
}
