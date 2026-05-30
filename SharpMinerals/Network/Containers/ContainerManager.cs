using SharpMinerals.Blocks;
using SharpMinerals.Components;
using SharpMinerals.Entities.Components;
using SharpMinerals.Items;
using SharpMinerals.Math;
using SharpMinerals.Network.Messages;

namespace SharpMinerals.Network.Containers;

/// <summary>
/// Server-authoritative container windows: opening a chest, applying clicks against the
/// shared chest storage + the clicker's inventory, and keeping every viewer of the same
/// chest in sync. Protocol-agnostic — it speaks our <see cref="ItemStack"/>/<see
/// cref="Inventory"/> and emits version-neutral container messages; the protocol's codecs
/// handle the wire (incl. mapping our per-window <c>Revision</c> to the State ID).
/// </summary>
public sealed class ContainerManager {
    // generic_9x3 (single chest) in the minecraft:menu registry.
    const int ChestMenuType = 2;
    const int ChestSize = 27;
    const int ChestWindowSlots = ChestSize + EntityInventory.MainSize; // 27 chest + 36 player = 63

    readonly object gate = new();
    readonly Dictionary<ulong, OpenWindow> openByClient = new();
    int nextWindowId;

    sealed class OpenWindow {
        public required ulong ClientId;
        public required int WindowId;
        public int Revision;
        public ItemStack Cursor;
        public required BlockEntity Chest;
        public required Inventory ChestInv;
    }

    int NextWindowId() => nextWindowId = nextWindowId % 100 + 1; // cycle 1..100

    // ── Public API ──────────────────────────────────────────────────────────

    /// <summary>Opens a chest container for a player and registers them as a viewer.</summary>
    public void Open(Server server, ulong clientId, BlockEntity chest) {
        lock (gate) {
            var pinv = PlayerInv(server, clientId);
            if (pinv is null) return;

            var chestInv = ChestInventory(chest);

            // One open container per client; close any prior one first.
            if (openByClient.Remove(clientId, out var prior) && !prior.Cursor.IsEmpty)
                PutIntoInventory(pinv, ref prior.Cursor);

            var w = new OpenWindow {
                ClientId = clientId, WindowId = NextWindowId(),
                Chest = chest, ChestInv = chestInv,
            };
            openByClient[clientId] = w;

            server.NetServer.Send(clientId, new OpenScreenS2C(w.WindowId, ChestMenuType, "Chest"));
            SendContent(server, w, pinv);
        }
    }

    /// <summary>Applies a click, resyncs the clicker, and fans chest changes to other viewers.</summary>
    public void OnClick(Server server, ulong clientId, ClickContainerC2S msg) {
        lock (gate) {
            if (!openByClient.TryGetValue(clientId, out var w) || w.WindowId != msg.WindowId) return;
            var pinv = PlayerInv(server, clientId);
            if (pinv is null) return;

            ApplyClick(w, pinv, msg);
            w.Revision++;

            SendContent(server, w, pinv);          // authoritative resync of the clicker
            BroadcastToOtherViewers(server, w);    // others viewing this chest
        }
    }

    /// <summary>Player closed the window: return the cursor to their inventory and forget the session.</summary>
    public void OnClose(Server server, ulong clientId, int windowId) {
        lock (gate) {
            if (!openByClient.TryGetValue(clientId, out var w) || w.WindowId != windowId) return;
            var pinv = PlayerInv(server, clientId);
            if (pinv is not null && !w.Cursor.IsEmpty) PutIntoInventory(pinv, ref w.Cursor);
            openByClient.Remove(clientId);
        }
    }

    /// <summary>A disconnecting player: drop any open session (cursor is lost with the connection).</summary>
    public void OnLeave(ulong clientId) {
        lock (gate) openByClient.Remove(clientId);
    }

    /// <summary>A chest block was broken: force every viewer's window closed.</summary>
    public void ForceCloseChest(Server server, Vector3i chestPos) {
        lock (gate) {
            foreach (var (clientId, w) in openByClient.Where(kv => kv.Value.Chest.Position == chestPos).ToList()) {
                server.NetServer.Send(clientId, new CloseContainerS2C(w.WindowId));
                openByClient.Remove(clientId);
            }
        }
    }

    // ── Click application ─────────────────────────────────────────────────────

    void ApplyClick(OpenWindow w, EntityInventory pinv, ClickContainerC2S msg) {
        // Modes: 0 = normal (button 0 left, 1 right); 1 = shift-move. Others resync only.
        if (msg.Slot == -999) return; // drop-outside not modelled yet (cursor returns on close)

        switch (msg.Mode) {
            case 0 when TryResolve(w, pinv, msg.Slot, out var inv, out var idx):
                if (msg.Button == 0) LeftClick(ref inv[idx], ref w.Cursor);
                else if (msg.Button == 1) RightClick(ref inv[idx], ref w.Cursor);
                break;
            case 1:
                ShiftMove(w, pinv, msg.Slot);
                break;
        }
    }

    static void LeftClick(ref ItemStack slot, ref ItemStack cursor) {
        if (cursor.IsEmpty) { cursor = slot; slot = default; return; }
        if (slot.IsEmpty) { slot = cursor; cursor = default; return; }
        if (slot.Type == cursor.Type) {
            int room = slot.Type!.MaxStackSize - slot.Count;
            int move = System.Math.Min(cursor.Count, room);
            if (move > 0) { slot.Count += move; cursor.Count -= move; if (cursor.Count <= 0) cursor = default; return; }
        }
        (slot, cursor) = (cursor, slot); // swap
    }

    static void RightClick(ref ItemStack slot, ref ItemStack cursor) {
        if (cursor.IsEmpty) {
            if (slot.IsEmpty) return;
            int half = (slot.Count + 1) / 2;
            cursor = new ItemStack(slot.Type!, half) { Data = slot.Data };
            slot.Count -= half;
            if (slot.Count <= 0) slot = default;
            return;
        }
        if (slot.IsEmpty) {
            slot = new ItemStack(cursor.Type!, 1) { Data = cursor.Data };
            cursor.Count--; if (cursor.Count <= 0) cursor = default;
            return;
        }
        if (slot.Type == cursor.Type && slot.Count < slot.Type!.MaxStackSize) {
            slot.Count++; cursor.Count--; if (cursor.Count <= 0) cursor = default;
            return;
        }
        (slot, cursor) = (cursor, slot);
    }

    void ShiftMove(OpenWindow w, EntityInventory pinv, int windowSlot) {
        if (!TryResolve(w, pinv, windowSlot, out var inv, out var idx)) return;
        ref var src = ref inv[idx];
        if (src.IsEmpty) return;
        // Chest slots (0-26) move to the player area (27-62) and vice versa.
        var (start, end) = windowSlot < ChestSize ? (ChestSize, ChestWindowSlots) : (0, ChestSize);
        MoveInto(w, pinv, ref src, start, end);
    }

    void MoveInto(OpenWindow w, EntityInventory pinv, ref ItemStack src, int start, int end) {
        for (int ws = start; ws < end && !src.IsEmpty; ws++)         // merge into same type
            if (TryResolve(w, pinv, ws, out var inv, out var idx)) {
                ref var dst = ref inv[idx];
                if (!dst.IsEmpty && dst.Type == src.Type && dst.Count < dst.Type!.MaxStackSize) {
                    int move = System.Math.Min(src.Count, dst.Type.MaxStackSize - dst.Count);
                    dst.Count += move; src.Count -= move; if (src.Count <= 0) src = default;
                }
            }
        for (int ws = start; ws < end && !src.IsEmpty; ws++)         // then first empty
            if (TryResolve(w, pinv, ws, out var inv, out var idx)) {
                ref var dst = ref inv[idx];
                if (dst.IsEmpty) { dst = src; src = default; }
            }
    }

    // Returns the cursor to the player's inventory; anything that doesn't fit is dropped for v1.
    static void PutIntoInventory(EntityInventory pinv, ref ItemStack stack) => stack = pinv.Add(stack);

    // ── Slot layout (chest window): 0-26 chest, 27-53 player main(9-35), 54-62 hotbar(0-8) ──
    static bool TryResolve(OpenWindow w, EntityInventory pinv, int windowSlot, out Inventory inv, out int index) {
        if (windowSlot is >= 0 and < ChestSize) { inv = w.ChestInv; index = windowSlot; return true; }
        if (windowSlot is >= ChestSize and < ChestSize + 27) { inv = pinv.Storage; index = 9 + (windowSlot - ChestSize); return true; } // 27-53 -> storage 9-35
        if (windowSlot is >= ChestSize + 27 and < ChestWindowSlots) { inv = pinv.Storage; index = windowSlot - (ChestSize + 27); return true; } // 54-62 -> storage 0-8
        inv = null!; index = 0; return false;
    }

    // ── Sending ───────────────────────────────────────────────────────────────

    void SendContent(Server server, OpenWindow w, EntityInventory pinv) {
        var slots = new ItemStack[ChestWindowSlots];
        for (int i = 0; i < ChestWindowSlots; i++)
            slots[i] = TryResolve(w, pinv, i, out var inv, out var idx) ? inv[idx] : default;
        server.NetServer.Send(w.ClientId, new SetContainerContentS2C(w.WindowId, w.Revision, slots, w.Cursor));
    }

    void BroadcastToOtherViewers(Server server, OpenWindow clicked) {
        foreach (var w in openByClient.Values)
            if (w.ClientId != clicked.ClientId && w.Chest.Position == clicked.Chest.Position) {
                var pinv = PlayerInv(server, w.ClientId);
                if (pinv is not null) { w.Revision++; SendContent(server, w, pinv); }
            }
    }

    // ── Player inventory window (id 0): 0-4 crafting, 5-8 armor, 9-35 main, 36-44 hotbar, 45 offhand ──
    public const int PlayerWindowSlots = 46;

    /// <summary>Builds the 46-slot player window (id 0) from an inventory.</summary>
    public static ItemStack[] PlayerWindow(EntityInventory inv) {
        var slots = new ItemStack[PlayerWindowSlots];
        slots[5] = inv.Armor(ArmorSlot.Head);
        slots[6] = inv.Armor(ArmorSlot.Chest);
        slots[7] = inv.Armor(ArmorSlot.Legs);
        slots[8] = inv.Armor(ArmorSlot.Feet);
        for (int i = 9; i <= 35; i++) slots[i] = inv.Main(i);
        for (int i = 0; i < 9; i++) slots[36 + i] = inv.Main(i);
        slots[45] = inv.Offhand;
        return slots;
    }

    /// <summary>Maps a player-window (id 0) slot to its backing storage index, if it has one.</summary>
    public static bool TryPlayerWindowToStorage(int windowSlot, out int storageIndex) {
        switch (windowSlot) {
            case >= 5 and <= 8: storageIndex = 44 - windowSlot; return true;   // 5(Head)->39 .. 8(Feet)->36
            case >= 9 and <= 35: storageIndex = windowSlot; return true;        // main storage
            case >= 36 and <= 44: storageIndex = windowSlot - 36; return true;  // hotbar -> 0-8
            case 45: storageIndex = EntityInventory.MainSize + EntityInventory.ArmorSize; return true; // offhand (40)
            default: storageIndex = 0; return false;                            // crafting / outside
        }
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    static EntityInventory? PlayerInv(Server server, ulong clientId) =>
        server.TryGetPlayer(clientId, out var h) && h.World.Ecs.IsAlive(h.Entity)
            ? h.World.Ecs.Get<EntityInventory>(h.Entity)
            : null;

    static Inventory ChestInventory(BlockEntity chest) {
        if (chest.TryGet<Inventory>(out var inv)) return inv;
        inv = new Inventory(ChestSize);
        chest.With(inv);
        return inv;
    }
}
