using Brigadier.NET;
using Brigadier.NET.Builder;
using SharpMinerals.Entities.Components;
using SharpMinerals.Events;
using SharpMinerals.Network.Containers;
using SharpMinerals.Network.Messages;

namespace SharpMinerals.Commands;

/// <summary><c>/clear</c> - empties the issuing player's whole inventory (main, armor, off-hand) and resyncs
/// the window. Player-only.</summary>
public static class ClearCommand {
    public static CommandDispatcher RegisterClear(this CommandDispatcher d) => d.Register(l => l
        .Literal("clear")
        .Requires(x => x.IsPlayer)
        .Executes(ctx => {
            var server = ctx.Source.Server;
            if (ctx.Source.Client is not { } client || 
                !server.TryGetPlayer(client.Id, out var context) ||
                !context.World.Ecs.IsAlive(context.Entity)
            ) {
                ctx.Source.Reply("Only an online player can clear their inventory.");
                return 0;
            }

            var inventory = context.World.Ecs.Get<InventoryEntityComponent>(context.Entity);
            int cleared = 0;
            for (int i = 0; i < inventory.Storage.Size; i++)
                if (!inventory.Storage[i].IsEmpty) cleared++;
            inventory.Storage.Clear();

            // Resync the window; equipment others see is refreshed by the per-tick equipment diff.
            context.SyncInventory();

            ctx.Source.Reply(cleared > 0 ? $"Cleared {cleared} item stack(s)." : "Inventory already empty.");
            return 1;
        }));
}
