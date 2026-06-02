using SharpMinerals.Blocks.Descriptors;
using SharpMinerals.Components;
using SharpMinerals.Items;
using SharpMinerals.Items.Components;

namespace SharpMinerals.Blocks;

/// <summary>A registered block type — a flyweight definition assembled from components, and (since
/// <c>BlockType : ItemType</c>) also an item. <see cref="Id"/> is the block id stored in chunks. A block
/// places itself by default; drops are NOT automatic without a <see cref="DropBlockDescriptor"/>.</summary>
public class BlockType : ItemType {
    /// <summary>A field (not a component lookup) because it's on the hot serialization path.</summary>
    public bool IsAir { get; }

    /// <summary>The stack dropped when broken, if the block has a <see cref="DropBlockDescriptor"/> component (no automatic self-drop).</summary>
    public ItemStack? Drop => TryGet<DropBlockDescriptor>(out var d) ? d.Stack : null;

    /// <summary>A block places itself by default (or a <see cref="Placeable"/> override); air places nothing.</summary>
    public override BlockType? PlacedBlock => IsAir ? null : TryGet<Placeable>(out var p) ? p.Block : this;

    public BlockType DropSelf() => this.Add(new DropBlockDescriptor(() => new ItemStack(this)));

    internal BlockType(int id, string name, bool isAir) : base(id, name) {
        IsAir = isAir;
    }
}
