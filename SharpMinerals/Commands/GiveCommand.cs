using Brigadier.NET;
using Brigadier.NET.Builder;
using Brigadier.NET.Context;
using SharpMinerals.Blocks;
using SharpMinerals.Entities.Components;
using SharpMinerals.Events;
using SharpMinerals.Items;
using SharpMinerals.Network.Containers;
using SharpMinerals.Network.Messages;

namespace SharpMinerals.Commands;

/// <summary>
/// <c>/give &lt;item&gt; [count]</c> — adds an item to the issuing player's inventory by registry name
/// (blocks and items, tab-completed), then resyncs the window. Player-only (the console has no inventory).
/// <paramref name="count"/> defaults to 1 and merges into / fills slots; whatever doesn't fit is dropped.
/// </summary>
public static class GiveCommand {
    const int MaxCount = 6400; // generous cap — the inventory keeps only what fits, the rest is reported as not given

    public static CommandDispatcher RegisterGive(this CommandDispatcher d) => d.Register(l => l
        .Literal("give")
        .Requires(x => x.IsPlayer)
        .Then(x => x.Argument("item", Arguments.Word())
            .Suggests((ctx, builder) => {
                // Suggest every giveable registry name (blocks except air, plus non-block items).
                foreach (var block in BlockRegistry.All)
                    if (!block.IsAir && block.Name.StartsWith(builder.Remaining, StringComparison.OrdinalIgnoreCase))
                        builder.Suggest(block.Name);
                foreach (var item in ItemRegistry.All)
                    if (item.Name.StartsWith(builder.Remaining, StringComparison.OrdinalIgnoreCase))
                        builder.Suggest(item.Name);
                return builder.BuildFuture();
            })
            .Executes(c => Give(c, 1))
            .Then(b => b.Argument("count", Arguments.Integer(1, MaxCount))
                .Executes(c => Give(c, Arguments.GetInteger(c, "count"))))));

    static int Give(CommandContext<SenderContext> ctx, int count) {
        var server = ctx.Source.Server;
        if (ctx.Source.Client is not { } client
            || !server.TryGetPlayer(client.Id, out var context)
            || !context.World.Ecs.IsAlive(context.Entity)) {
            ctx.Source.Reply("Only an online player can be given items.");
            return 0;
        }

        var name = Arguments.GetString(ctx, "item");
        if (ItemRegistry.Resolve(name) is not { } type || type is BlockType { IsAir: true }) {
            ctx.Source.Reply($"Unknown item '{name}'.");
            return 0;
        }

        var inventory = context.World.Ecs.Get<InventoryEntityComponent>(context.Entity);
        var leftover = inventory.Add(new ItemStack(type, count));
        int given = count - leftover.Count;
        if (given <= 0) { ctx.Source.Reply("Your inventory is full."); return 0; }

        // Push the updated window (cursor cleared) and refresh the equipment others see (held/armour).
        client.Send(new SetContainerContentS2C(0, 0, ContainerManager.PlayerWindow(inventory), default));
        server.Events.Publish(new PlayerInventoryChanged(context));

        ctx.Source.Reply($"Gave x{given} {type.Name}.");
        return given;
    }
}
