using SharpMinerals.Blocks;
using SharpMinerals.Entities.Components;
using SharpMinerals.Level;
using SharpMinerals.Math;
using SharpMinerals.Network.Messages;

namespace SharpMinerals.Network.Containers;

/// <summary>Owns every open window for one <see cref="World"/>: chest sessions (one per client, backed
/// by an <see cref="InventoryWindow"/>) and each client's always-open player-inventory window
/// (a <see cref="CustomWindow"/>, window id 0). Purely event-driven - opens/clicks/closes
/// arrive straight from network handling, so there's nothing to do on <c>Tick</c>/<c>Flush</c>.</summary>
public sealed class ContainerManager(Server server) {
    Server server = server;
    // generic_9x3 (single chest) in the minecraft:menu registry.
    const int ChestMenuType = 2;
    const int ChestSize = 27;
    const int PlayerWindowId = 0;

    // Chest "block action": action 1 = set number of viewers; the client opens the lid while > 0 and closes it at 0.
    const int ChestViewersAction = 1;

    readonly object gate = new();
    readonly Dictionary<ulong, ChestSession> openByClient = [];
    readonly Dictionary<ulong, CustomWindow> playerWindows = [];
    int nextWindowId;

    sealed class ChestSession {
        public required InventoryWindow Handler;
        public required BlockEntity Chest;
    }

    int NextWindowId() => nextWindowId = (nextWindowId & 0x7f) + 1; // cycle 1..127

    // -- Public API ----------------------------------------------------------

    /// <summary>Registers the player as a viewer of the chest. <paramref name="chest"/> must belong to
    /// this system's world - the caller is responsible for routing to the right world's instance.</summary>
    public void Open(ulong clientId, BlockEntity chest) {
        lock(gate) {
            var world = chest.World;
            if(!server.TryGetPlayer(clientId, out var ctx) || !ctx.World.Ecs.IsAlive(ctx.Entity))
                return;
            if(ctx.World != world)
                return; // wired to the wrong world's manager

            var pinv = ctx.World.Ecs.Get<InventoryEntityComponent>(ctx.Entity);
            var chestInv = ChestInventory(chest);

            // One open container per client; close any prior one first.
            if(openByClient.Remove(clientId, out var prior) && !prior.Handler.Cursor.IsEmpty)
                ReturnCursor(prior.Handler, pinv);

            var handler = new InventoryWindow(clientId, NextWindowId(), chestInv, ChestSize, () => PlayerInv(server, clientId));
            openByClient[clientId] = new ChestSession { Handler = handler, Chest = chest };

            server.NetServer.Send(clientId, new OpenScreenS2C(handler.WindowId, ChestMenuType, "Chest"));
            SendContent(handler);

            // Animate lids: the prior chest (if a different one) lost a viewer, and this one gained one.
            if(prior is not null && prior.Chest.Position != chest.Position)
                UpdateChestViewers(prior.Chest.World, prior.Chest.Position);
            UpdateChestViewers(chest.World, chest.Position);
        }
    }

    /// <summary>Applies a click, resyncs the clicker, and fans chest changes to other viewers. Window 0
    /// (the player's own inventory) routes to its own always-open handler instead.</summary>
    public void OnClick(ulong clientId, ClickContainerC2S msg) {
        lock(gate) {
            var pinv = PlayerInv(server, clientId);
            if(pinv is null)
                return;

            if(msg.WindowId == PlayerWindowId) {
                var window = PlayerWindow(clientId);
                if(window.HandleClick(server, pinv, msg)) { window.MarkDirty(); SendContent(window); }
                return;
            }

            if(!openByClient.TryGetValue(clientId, out var session) || session.Handler.WindowId != msg.WindowId)
                return;
            if(session.Handler.HandleClick(server, pinv, msg)) {
                session.Handler.MarkDirty();
                SendContent(session.Handler);          // authoritative resync of the clicker
                BroadcastToOtherViewers(session);       // others viewing this chest
            }
        }
    }

    /// <summary>Player closed a window: return the cursor to their inventory. Window 0 has no real
    /// "open" session to forget - it just flushes its cursor, if the client ever sends this for it.</summary>
    public void OnClose(ulong clientId, int windowId) {
        lock(gate) {
            if(windowId == PlayerWindowId) {
                if(playerWindows.TryGetValue(clientId, out var window) && !window.Cursor.IsEmpty)
                    ReturnCursor(window, PlayerInv(server, clientId));
                return;
            }
            if(!openByClient.TryGetValue(clientId, out var session) || session.Handler.WindowId != windowId)
                return;
            ReturnCursor(session.Handler, PlayerInv(server, clientId));
            openByClient.Remove(clientId);
            UpdateChestViewers(session.Chest.World, session.Chest.Position); // this viewer left -> maybe close the lid
        }
    }

    /// <summary>A disconnecting player: drop any open session (cursor is lost with the connection).</summary>
    public void OnLeave(ulong clientId) {
        lock(gate) {
            if(openByClient.Remove(clientId, out var session))
                UpdateChestViewers(session.Chest.World, session.Chest.Position); // last viewer may have gone
            playerWindows.Remove(clientId); // its cursor vanishes with the connection, as in vanilla
        }
    }

    /// <summary>A chest block was broken: force every viewer's window closed.</summary>
    public void ForceCloseChest(World world, Vector3i chestPos) {
        lock(gate) {
            bool anyClosed = false;
            foreach(var (clientId, session) in openByClient.Where(kv => kv.Value.Chest.Position == chestPos).ToList()) {
                anyClosed = true;
                server.NetServer.Send(clientId, new CloseContainerS2C(session.Handler.WindowId));
                openByClient.Remove(clientId);
            }

            if(anyClosed) {
                UpdateChestViewers(world, chestPos);
            }
        }
    }

    // -- Helpers ---------------------------------------------------------------

    public CustomWindow PlayerWindow(ulong clientId) {
        if(!playerWindows.TryGetValue(clientId, out var window)) {
            window = CustomWindow.ForPlayerInventory(clientId, PlayerInv(server, clientId));
            playerWindows[clientId] = window;
        }
        return window;
    }

    static void ReturnCursor(BaseWindow handler, InventoryEntityComponent? pinv) {
        if(pinv is not null && !handler.Cursor.IsEmpty)
            handler.Cursor = pinv.Add(handler.Cursor);
    }

    // Broadcasts the chest's current open-viewer count so nearby clients animate its lid (open > 0, closed at 0).
    void UpdateChestViewers(World world, Vector3i chest) {
        int viewers = openByClient.Values.Count(s => s.Chest.Position == chest);
        server.BroadcastInRange(world, chest.X + 0.5, chest.Z + 0.5,
            new BlockActionS2C(chest, ChestViewersAction, (byte)System.Math.Min(viewers, byte.MaxValue)));
    }

    void SendContent(BaseWindow window) =>
        server.NetServer.Send(window.ClientId, new SetContainerContentS2C(window.WindowId, window.Revision, window.BuildSnapshot(), window.Cursor));

    void BroadcastToOtherViewers(ChestSession clicked) {
        foreach(var session in openByClient.Values)
            if(session.Handler.ClientId != clicked.Handler.ClientId && session.Chest.Position == clicked.Chest.Position) {
                session.Handler.MarkDirty();
                SendContent(session.Handler);
            }
    }

    /// <summary>Sends the full player inventory window (window 0) to the client.</summary>
    public void SendPlayerInventory(ulong clientId) {
        lock(gate) {
            var window = PlayerWindow(clientId);
            server.NetServer.Send(clientId, new SetContainerContentS2C(0, window.Revision, window.BuildSnapshot(), window.Cursor));
        }
    }

    /// <summary>Forces the current chest (if any) closed, sending a close packet to the client.</summary>
    public void CloseCurrentChestWithPacket(ulong clientId) {
        lock(gate) {
            if(openByClient.TryGetValue(clientId, out var session)) {
                ReturnCursor(session.Handler, PlayerInv(server, clientId));
                openByClient.Remove(clientId);
                UpdateChestViewers(session.Chest.World, session.Chest.Position);
                server.NetServer.Send(clientId, new CloseContainerS2C(session.Handler.WindowId));
            }
        }
    }

    // Deliberately re-resolved on every call (not cached) - the player may have respawned or changed
    // worlds since the last click, which can swap out their live ECS component/entity.
    static InventoryEntityComponent? PlayerInv(Server server, ulong clientId) =>
        server.TryGetPlayer(clientId, out var h) && h.World.Ecs.IsAlive(h.Entity)
            ? h.World.Ecs.Get<InventoryEntityComponent>(h.Entity)
            : null;

    static InventoryComponent ChestInventory(BlockEntity chest) {
        if(chest.TryGet<InventoryComponent>(out var inv))
            return inv;
        inv = new InventoryComponent(ChestSize);
        chest.Add(inv);
        return inv;
    }

    /// <summary>Window id 0.</summary>
    public static bool TryPlayerWindowToStorage(int windowSlot, out int storageIndex) {
        switch(windowSlot) {
            case >= 5 and <= 8:
                storageIndex = 44 - windowSlot;
                return true;   // 5(Head)->39 .. 8(Feet)->36
            case >= 9 and <= 35:
                storageIndex = windowSlot;
                return true;        // main storage
            case >= 36 and <= 44:
                storageIndex = windowSlot - 36;
                return true;  // hotbar -> 0-8
            case 45:
                storageIndex = InventoryEntityComponent.MainSize + InventoryEntityComponent.ArmorSize;
                return true; // offhand (40)
            default:
                storageIndex = 0;
                return false;                            // crafting / outside
        }
    }
}