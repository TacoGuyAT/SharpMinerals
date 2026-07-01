using SharpMinerals.Blocks;
using SharpMinerals.Chat;
using SharpMinerals.Entities.Components;
using SharpMinerals.Items;
using SharpMinerals.Level;
using SharpMinerals.Network;
using SharpMinerals.Network.Containers;
using SharpMinerals.Network.Messages;
using ArchEntity = Arch.Core.Entity;

namespace SharpMinerals.Events.Contexts;

/// <summary>An <see cref="EntityContext"/> for a player entity, adding the network <see cref="Client"/>
/// connection. Carried by player events; <see cref="GetPlayer"/> reads the live component from the world.</summary>
public sealed record PlayerContext(Server Server, World World, ArchEntity Entity, NetClient Client)
    : EntityContext(Server, World, Entity) {
    /// <summary>
    /// This method queries ECS
    /// </summary>
    public ref PlayerEntityComponent GetPlayer() => ref World.Ecs.Get<PlayerEntityComponent>(Entity);

    /// <summary>
    /// This method queries ECS
    /// </summary>
    public ref InventoryEntityComponent GetInventory() => ref World.Ecs.Get<InventoryEntityComponent>(Entity);

    /// <summary>
    /// The held (selected hotbar) item, by reference, so callers can read or mutate it in place.
    /// This method queries ECS
    /// </summary>
    public ref ItemStack GetHeld() => ref GetInventory().Held;

    /// <summary>Opens a chest (block container) for this player.</summary>
    public void OpenChest(BlockEntity chest)
        => Server.Containers.Open(Client.Id, chest);

    /// <summary>Forces the current chest (if any) closed, sending a close packet to the client.</summary>
    public void CloseChest()
        => Server.Containers.CloseCurrentChestWithPacket(Client.Id);

    /// <summary>Pushes the player's whole inventory window (window 0) to the client.</summary>
    public void SyncInventory()
        => Server.Containers.SendPlayerInventory(Client.Id);

    /// <summary>Consumes up to <paramref name="amount"/> from the player's held stack and resyncs the inventory
    /// to the client. Returns the number actually removed (0 if the hand was empty).</summary>
    public int ConsumeHeld(int amount = 1) {
        int removed = GetInventory().ConsumeHeld(amount);
        if (removed > 0) SyncInventory(); // TODO: sync single slot
        return removed;
    }

    /// <summary>Shows <paramref name="text"/> on the player's action bar (the line above the hotbar).</summary>
    public void SendActionBar(TextComponent text) => Client.Send(new SystemChatMessageS2C(text, true));

    /// <summary>Shows <paramref name="text"/> on the player's action bar.</summary>
    public void SendActionBar(string text) => SendActionBar(new TextComponent(text));

    /// <summary>TODO</summary>
    public void SendChat(TextComponent text) => Client.Send(new SystemChatMessageS2C(text, false));

    /// <summary>TODO</summary>
    public void SendChat(string text) => SendChat(new TextComponent(text));
}
