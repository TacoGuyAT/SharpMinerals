using SharpMinerals.Items;

namespace SharpMinerals.Blocks.Descriptors;

/// <summary>Marks a block subject to gravity (sand, gravel): when it loses its support it detaches into a
/// <c>falling_block</c> entity that re-becomes a block when it lands.</summary>
public sealed class FallingBlockDescriptor : IOnLand {
    public void OnLand(in BlockContext ctx) {
        if (ctx.World.GetBlock(ctx.Position).IsAir)
            ctx.World.SetBlock(ctx.Position, ctx.Block);
        else
            ctx.World.SpawnDroppedItem(ctx.Position, new ItemStack(ctx.Block)); // cell occupied — pop as an item
    }
}
