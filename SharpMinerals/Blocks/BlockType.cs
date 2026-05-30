using SharpMinerals.Blocks.Components;
using SharpMinerals.Items;

namespace SharpMinerals.Blocks;

/// <summary>
/// A registered block type — a flyweight definition composed of components, and (since
/// <c>BlockType : ItemType</c>) also an item. <see cref="Id"/> is the SharpMinerals
/// block id stored in chunks. <see cref="IsAir"/> is a core gameplay field; the Java
/// Edition wire ids are NOT here — that mapping is a network concern (see the JE763
/// <c>VanillaMapping</c> translator).
/// </summary>
public class BlockType : ItemType {
    /// <summary>Whether this block is air. Core field for the hot serialization path.</summary>
    public bool IsAir { get; }

    /// <summary>The item dropped when this block is broken, if any (from <see cref="Drops"/>).</summary>
    public ItemType? Drop => TryGet<Drops>(out var d) ? d.Item : null;

    internal BlockType(int id, string name, bool isAir) : base(id, name) {
        IsAir = isAir;
    }
}
