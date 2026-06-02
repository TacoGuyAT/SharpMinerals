using SharpMinerals.Components;

namespace SharpMinerals.Blocks.Descriptors;

/// <summary>Marks a block that opens a container window when used. Carries a block entity (its contents).</summary>
public sealed class ContainerBlockDescriptor : IInteract, IOnBroken, IBlockEntityDescriptor {
    public int Size;
    public ContainerBlockDescriptor(int size) => Size = size;

    /// <summary>Gives a freshly-created container block entity its backing inventory (run once via the
    /// <see cref="Level.World.GetOrCreateBlockEntity"/> funnel), so the size lives with the descriptor.</summary>
    public void Initialize(BlockEntity blockEntity) => blockEntity.Add(new InventoryComponent(Size));

    public void OnInteract(in BlockContext ctx) {
        if (ctx.Actor is not { } actor) return;
        if (ctx.World.GetOrCreateBlockEntity(ctx.Position) is { } chest)
            actor.Server.Containers.Open(actor.Server, actor.Client.Id, chest);
    }

    public void OnBroken(in BlockContext ctx) =>
        ctx.Actor?.Server.Containers.ForceCloseChest(ctx.Actor.Server, ctx.Position);
}
