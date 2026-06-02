namespace SharpMinerals.Blocks.Descriptors;

/// <summary>Marks a block that opens a container window when used. Carries a block entity (its contents).</summary>
public sealed class ContainerBlockDescriptor : IInteract, IOnBroken, IBlockEntityDescriptor {
    public int Size;
    public ContainerBlockDescriptor(int size) => Size = size;

    public void OnInteract(in BlockContext ctx) {
        if (ctx.Actor is not { } actor) return;
        var chest = ctx.World.GetBlockEntity(ctx.Position) ?? ctx.World.CreateBlockEntity(ctx.Position, ctx.Block);
        actor.Server.Containers.Open(actor.Server, actor.Client.Id, chest);
    }

    public void OnBroken(in BlockContext ctx) =>
        ctx.Actor?.Server.Containers.ForceCloseChest(ctx.Actor.Server, ctx.Position);
}
