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
/// connection. Carried by player events; <see cref="Player"/> reads the live component from the world.</summary>
public sealed record PlayerContext(Server Server, World World, ArchEntity Entity, NetClient Client)
    : EntityContext(Server, World, Entity) {
    public NetPlayerEntityComponent Player => World.Ecs.Get<NetPlayerEntityComponent>(Entity);

    /// <summary>The player's inventory.</summary>
    public InventoryEntityComponent Inventory => World.Ecs.Get<InventoryEntityComponent>(Entity);

    /// <summary>The held (selected hotbar) item, by reference, so callers can read or mutate it in place.</summary>
    public ref ItemStack Held => ref Inventory.Held;

    /// <summary>Pushes the player's whole inventory window (window 0) back to the client - call after a
    /// server-side change to its contents so the client's view stays in sync.</summary>
    public void SyncInventory() =>
        Client.Send(new SetContainerContentS2C(0, 0, ContainerManager.PlayerWindow(Inventory), default));

    /// <summary>Consumes up to <paramref name="amount"/> from the player's held stack and resyncs the inventory
    /// to the client. Returns the number actually removed (0 if the hand was empty).</summary>
    public int ConsumeHeld(int amount = 1) {
        int removed = Inventory.ConsumeHeld(amount);
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
