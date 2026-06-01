using Brigadier.NET;
using Brigadier.NET.Builder;
using SharpMinerals.Entities.Components;
using SharpMinerals.Events;
using SharpMinerals.Network.Containers;
using SharpMinerals.Network.Messages;

namespace SharpMinerals.Commands;

/// <summary><c>/clear</c> — empties the issuing player's whole inventory (main, armor, and off-hand), then
/// resyncs the window so the client's view clears and any visible equipment update reaches other players.
/// Player-only (the console has no inventory).</summary>
public static class ClearCommand {
    public static CommandDispatcher RegisterClear(this CommandDispatcher d) => d.Register(l => l
        .Literal("clear")
        .Requires(s => s.IsPlayer)
        .Executes(c => {
            var server = c.Source.Server;
            if (server is null) { c.Source.Reply("Server is not running."); return 0; }
            if (c.Source.Client is not { } client
                || !server.TryGetPlayer(client.Id, out var context)
                || !context.World.Ecs.IsAlive(context.Entity)) {
                c.Source.Reply("Only an online player can clear their inventory.");
                return 0;
            }

            var inventory = context.World.Ecs.Get<InventoryEntityComponent>(context.Entity);
            int cleared = 0;
            for (int i = 0; i < inventory.Storage.Size; i++)
                if (!inventory.Storage[i].IsEmpty) cleared++;
            inventory.Storage.Clear();

            // Push the emptied window to the client (cursor cleared too) and let the equipment-visibility
            // subscriber refresh what others see now that held/armor are gone.
            client.Send(new SetContainerContentS2C(0, 0, ContainerManager.PlayerWindow(inventory), default));
            server.Events.Publish(new PlayerInventoryChanged(context));

            c.Source.Reply(cleared > 0 ? $"Cleared {cleared} item stack(s)." : "Inventory already empty.");
            return 1;
        }));
}
