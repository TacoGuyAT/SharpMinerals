using SharpMinerals.Blocks.Descriptors;
using SharpMinerals.Components;
using SharpMinerals.Items;
using SharpMinerals.Items.Components;

namespace SharpMinerals.Blocks;

/// <summary>A registered block type — a flyweight definition assembled from components, and (since it derives
/// from <see cref="ItemType"/>) also an item, registered in the one <see cref="ItemRegistry"/>. Its inherited
/// <see cref="ItemType.ItemId"/> is the unified item id; <see cref="BlockId"/> is the separate dense palette id
/// stored in chunks. A block places itself by default; drops are NOT automatic without a <see cref="DropBlockDescriptor"/>.</summary>
public class BlockType : ItemType {
    /// <summary>The dense palette id stored in chunks (distinct from the unified item <see cref="ItemType.ItemId"/>).</summary>
    internal int BlockId { get; }

    /// <summary>A field (not a component lookup) because it's on the hot serialization path.</summary>
    public bool IsAir { get; }

    bool? isBlockEntity;

    /// <summary>Whether this block carries a block entity (a tile entity) and so must be listed in the chunk
    /// packet even before its server-side instance is lazily created. Computed once from the presence of an
    /// <see cref="IBlockEntityDescriptor"/> and cached, because it's tested per non-air block on the hot
    /// serialization path. Block composition is finished before any block is serialized.</summary>
    public bool IsBlockEntity => isBlockEntity ??= GetAll<IBlockEntityDescriptor>().Any();

    /// <summary>The stack dropped when broken, if the block has a <see cref="DropBlockDescriptor"/> component (no automatic self-drop).</summary>
    public ItemStack? Drop => TryGet<DropBlockDescriptor>(out var d) ? d.Stack : null;

    /// <summary>A block places itself by default (or a <see cref="Placeable"/> override); air places nothing.</summary>
    public override BlockType? PlacedBlock => IsAir ? null : TryGet<Placeable>(out var p) ? p.Block : this;

    public BlockType DropSelf() => this.Add(new DropBlockDescriptor(() => new ItemStack(this)));

    internal BlockType(int itemId, int blockId, string name, bool isAir) : base(itemId, name) {
        BlockId = blockId;
        IsAir = isAir;
    }
}
