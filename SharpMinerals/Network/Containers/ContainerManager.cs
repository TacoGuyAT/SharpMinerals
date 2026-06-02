using SharpMinerals.Blocks;
using SharpMinerals.Components;
using SharpMinerals.Entities.Components;
using SharpMinerals.Events;
using SharpMinerals.Items;
using SharpMinerals.Math;
using SharpMinerals.Network.Handlers;
using SharpMinerals.Network.Messages;

namespace SharpMinerals.Network.Containers;

/// <summary>
/// Server-authoritative container windows: opening a chest, applying clicks against the shared chest
/// storage + the clicker's inventory, and keeping every viewer in sync. Protocol-agnostic; the codecs
/// handle the wire (incl. mapping our per-window <c>Revision</c> to the State ID).
/// </summary>
public sealed class ContainerManager {
    // generic_9x3 (single chest) in the minecraft:menu registry.
    const int ChestMenuType = 2;
    const int ChestSize = 27;
    const int ChestWindowSlots = ChestSize + InventoryEntityComponent.MainSize; // 27 chest + 36 player = 63

    const int PlayerWindowId = 0; // the always-open player inventory (no Open packet, never in openByClient)
    const int OffhandButton = 40; // mode-2 (number-key swap) button value that targets the off-hand
    const int OutsideSlot = -999; // a click (or drag start/end) outside any slot

    readonly object gate = new();
    readonly Dictionary<ulong, OpenWindow> openByClient = new();
    // The cursor for each client's player-inventory window (id 0); chest windows keep theirs on OpenWindow.
    readonly Dictionary<ulong, ItemStack> playerCursor = new();
    // The window slots painted by an in-progress mode-5 drag, per client (between its start and end events).
    readonly Dictionary<ulong, List<int>> dragSlots = new();
    int nextWindowId;

    sealed class OpenWindow {
        public required ulong ClientId;
        public required int Id;
        public int Revision;
        public ItemStack Cursor;
        public required BlockEntity Chest;
        public required InventoryComponent ChestInv;
    }

    int NextWindowId() => nextWindowId = nextWindowId % 100 + 1; // cycle 1..100

    // ── Public API ──────────────────────────────────────────────────────────

    /// <summary>Registers the player as a viewer of the chest.</summary>
    public void Open(Server server, ulong clientId, BlockEntity chest) {
        lock (gate) {
            var pinv = PlayerInv(server, clientId);
            if (pinv is null) return;

            var chestInv = ChestInventory(chest);

            // One open container per client; close any prior one first.
            if (openByClient.Remove(clientId, out var prior) && !prior.Cursor.IsEmpty)
                PutIntoInventory(pinv, ref prior.Cursor);

            var window = new OpenWindow {
                ClientId = clientId, Id = NextWindowId(),
                Chest = chest, ChestInv = chestInv,
            };
            openByClient[clientId] = window;

            server.NetServer.Send(clientId, new OpenScreenS2C(window.Id, ChestMenuType, "Chest"));
            SendContent(server, window, pinv);
        }
    }

    /// <summary>Applies a click, resyncs the clicker, and fans chest changes to other viewers. Window 0
    /// (the player's own inventory) takes the dedicated player-inventory path.</summary>
    public void OnClick(Server server, ulong clientId, ClickContainerC2S msg) {
        lock (gate) {
            if (msg.WindowId == PlayerWindowId) { OnPlayerInventoryClick(server, clientId, msg); return; }
            if (!openByClient.TryGetValue(clientId, out var window) || window.Id != msg.WindowId) return;
            var pinv = PlayerInv(server, clientId);
            if (pinv is null) return;

            if (msg.Mode == 5) {
                // A drag's start/add events change nothing yet; only its end distributes — resync only then.
                if (!HandleDrag(clientId, msg, s => TryResolve(window, pinv, s, out var inv, out var i) ? (inv, i) : null, ref window.Cursor))
                    return;
            } else {
                ApplyClick(server, window, pinv, msg);
            }
            window.Revision++;

            SendContent(server, window, pinv);          // authoritative resync of the clicker
            BroadcastToOtherViewers(server, window);    // others viewing this chest
            PublishInventoryChanged(server, clientId); // a click can move the held item — sync equipment
        }
    }

    /// <summary>Player closed a window: return the cursor to their inventory and forget the session.</summary>
    public void OnClose(Server server, ulong clientId, int windowId) {
        lock (gate) {
            if (windowId == PlayerWindowId) {
                // The player inventory has no session to forget; just return any held cursor to it.
                if (playerCursor.Remove(clientId, out var cursor) && !cursor.IsEmpty && PlayerInv(server, clientId) is { } pinv) {
                    PutIntoInventory(pinv, ref cursor);
                    PublishInventoryChanged(server, clientId);
                }
                return;
            }
            if (!openByClient.TryGetValue(clientId, out var window) || window.Id != windowId) return;
            var chestPinv = PlayerInv(server, clientId);
            if (chestPinv is not null && !window.Cursor.IsEmpty) PutIntoInventory(chestPinv, ref window.Cursor);
            openByClient.Remove(clientId);
        }
    }

    /// <summary>A disconnecting player: drop any open session (cursor is lost with the connection).</summary>
    public void OnLeave(ulong clientId) {
        lock (gate) {
            openByClient.Remove(clientId);
            playerCursor.Remove(clientId); // items on the cursor vanish with the connection, as in vanilla
            dragSlots.Remove(clientId);    // abandon any in-progress drag
        }
    }

    /// <summary>A chest block was broken: force every viewer's window closed.</summary>
    public void ForceCloseChest(Server server, Vector3i chestPos) {
        lock (gate) {
            foreach (var (clientId, w) in openByClient.Where(kv => kv.Value.Chest.Position == chestPos).ToList()) {
                server.NetServer.Send(clientId, new CloseContainerS2C(w.Id));
                openByClient.Remove(clientId);
            }
        }
    }

    // ── Click application ─────────────────────────────────────────────────────

    void ApplyClick(Server server, OpenWindow w, InventoryEntityComponent pinv, ClickContainerC2S msg) {
        if (msg.Mode == 1) {                           // shift-move targets are window-specific
            ShiftMove(w, pinv, msg.Slot);
            return;
        }
        if (msg.Slot == OutsideSlot) {                 // a click outside the window
            // Mode 0 with a held cursor drops it (whole on left, one on right); empty cursor is a no-op.
            if (msg.Mode == 0) DropStack(server, w.ClientId, ref w.Cursor, whole: msg.Button == 0);
            return;
        }
        if (!TryResolve(w, pinv, msg.Slot, out var inv, out var idx))
            return; // crafting / invalid slot
        Dispatch(server, w.ClientId, pinv, ref inv[idx], ref w.Cursor, msg);
    }

    /// <summary>A click on the player's own inventory window (id 0); mirrors the chest path but against the
    /// player-window slot layout (<see cref="TryPlayerWindowToStorage"/>) and a per-client cursor.</summary>
    void OnPlayerInventoryClick(Server server, ulong clientId, ClickContainerC2S msg) {
        var pinv = PlayerInv(server, clientId);
        if (pinv is null) return;
        var cursor = playerCursor.GetValueOrDefault(clientId);

        if (msg.Mode == 5) {
            // A drag's start/add events change nothing yet; only its end distributes — resync only then.
            bool ended = HandleDrag(clientId, msg, s => TryPlayerWindowToStorage(s, out var i) ? (pinv.Storage, i) : null, ref cursor);
            playerCursor[clientId] = cursor;
            if (!ended) return;
        } else if (msg.Slot == OutsideSlot) {
            if (msg.Mode == 0) DropStack(server, clientId, ref cursor, whole: msg.Button == 0); // drop the cursor
            playerCursor[clientId] = cursor;
        } else if (TryPlayerWindowToStorage(msg.Slot, out int idx)) {
            if (msg.Mode == 1) PlayerShiftMove(pinv, idx);
            else Dispatch(server, clientId, pinv, ref pinv.Storage[idx], ref cursor, msg);
            playerCursor[clientId] = cursor;
        }

        server.NetServer.Send(clientId, new SetContainerContentS2C(PlayerWindowId, 0, PlayerWindow(pinv), cursor));
        PublishInventoryChanged(server, clientId);
    }

    // Applies a resolved click against the hovered <paramref name="slot"/> and <paramref name="cursor"/>.
    // Mode 0 = normal (button 0 left, 1 right); 2 = number-key swap with a hotbar/off-hand slot (button =
    // target); 3 = creative clone to cursor; 4 = drop key. Modes 1 (shift-move) and 5 (drag) are window-
    // specific and handled by the callers; double-click (6) is not modelled.
    void Dispatch(Server server, ulong clientId, InventoryEntityComponent pinv, ref ItemStack slot, ref ItemStack cursor, ClickContainerC2S msg) {
        switch (msg.Mode) {
            case 0 when msg.Button == 0: LeftClick(ref slot, ref cursor); break;
            case 0 when msg.Button == 1: RightClick(ref slot, ref cursor); break;
            case 2 when HotbarSwapTarget(msg.Button, out var hb): Swap(ref slot, ref pinv.Storage[hb]); break;
            case 3 when !slot.IsEmpty && cursor.IsEmpty: cursor = CloneFull(slot); break; // creative middle-click
            case 4: DropStack(server, clientId, ref slot, whole: msg.Button == 1); break;
        }
    }

    // Processes a mode-5 drag event (start/add/end). Returns true only on the END event, after distributing
    // the cursor across the painted slots — the caller resyncs then (start/add change nothing visible yet).
    bool HandleDrag(ulong clientId, ClickContainerC2S msg, Func<int, (InventoryComponent Inv, int Index)?> resolve, ref ItemStack cursor) {
        switch (msg.Button) {
            case 0 or 4 or 8:                                    // start: left / right / middle
                dragSlots[clientId] = new List<int>();
                return false;
            case 1 or 5 or 9:                                    // add a painted slot
                if (msg.Slot != OutsideSlot && dragSlots.TryGetValue(clientId, out var painting) && !painting.Contains(msg.Slot))
                    painting.Add(msg.Slot);
                return false;
            case 2 or 6 or 10:                                   // end: distribute, then resync
                if (dragSlots.Remove(clientId, out var painted))
                    DistributeDrag(msg.Button, painted, resolve, ref cursor);
                return true;
            default:
                return false;
        }
    }

    // Distributes the cursor across a drag's painted slots: left (end button 2) splits the stack as evenly as
    // possible, right (6) places one item per slot, middle (10, creative) fills each slot with a full stack.
    // Whatever isn't placed stays on the cursor.
    static void DistributeDrag(int endButton, List<int> windowSlots, Func<int, (InventoryComponent Inv, int Index)?> resolve, ref ItemStack cursor) {
        if (cursor.IsEmpty) return;
        var targets = new List<(InventoryComponent Inv, int Index)>();
        foreach (int s in windowSlots)
            if (resolve(s) is { } t) targets.Add(t);
        if (targets.Count == 0) return;

        var type = cursor.Type!;
        int maxStack = type.MaxStackSize;

        if (endButton == 10) { // middle drag (creative): fill each empty slot with a full stack, cursor unchanged
            foreach (var (inv, idx) in targets)
                if (inv[idx].IsEmpty) inv[idx] = new ItemStack(type, maxStack) { Data = cursor.Data };
            return;
        }

        int total = cursor.Count;
        int perSlot = endButton == 2 ? total / targets.Count : 1; // left = even split, right = one each
        int placed = 0;
        foreach (var (inv, idx) in targets) {
            if (placed >= total || perSlot <= 0) break;
            ref var slot = ref inv[idx];
            int give;
            if (slot.IsEmpty) {
                give = System.Math.Min(perSlot, System.Math.Min(maxStack, total - placed));
                if (give <= 0) continue;
                slot = new ItemStack(type, give) { Data = cursor.Data };
            } else if (slot.StacksWith(cursor) && slot.Count < maxStack) {
                give = System.Math.Min(perSlot, System.Math.Min(maxStack - slot.Count, total - placed));
                if (give <= 0) continue;
                slot.Count += give;
            } else continue;
            placed += give;
        }
        cursor.Count = total - placed;
        if (cursor.Count <= 0) cursor = default;
    }

    static void Swap(ref ItemStack a, ref ItemStack b) => (a, b) = (b, a);

    // Number-key (mode 2) swap target → backing storage slot: button 0-8 = that hotbar slot, 40 = off-hand.
    static bool HotbarSwapTarget(int button, out int storageIndex) {
        if (button is >= 0 and <= 8) { storageIndex = button; return true; }
        if (button == OffhandButton) { storageIndex = InventoryEntityComponent.MainSize + InventoryEntityComponent.ArmorSize; return true; }
        storageIndex = 0; return false;
    }

    // A creative clone (mode 3): a full stack of the hovered item, keeping its carried state (e.g. wool colour).
    static ItemStack CloneFull(ItemStack item) {
        var full = item;
        full.Count = item.Type!.MaxStackSize;
        return full;
    }

    // Splits what to drop off a stack (the whole stack, or a single item) and tosses it from the player.
    static void DropStack(Server server, ulong clientId, ref ItemStack from, bool whole) {
        if (from.IsEmpty) return;
        var dropped = from;
        if (whole || from.Count <= 1) from = default;
        else { dropped.Count = 1; from.Count -= 1; }
        if (server.TryGetPlayer(clientId, out var ctx) && ctx.World.Ecs.IsAlive(ctx.Entity))
            ctx.World.TossItem(ctx.World.Ecs.Get<TransformEntityComponent>(ctx.Entity), dropped);
    }

    // Shift-click in the player's own inventory: hotbar↔main, and armour/off-hand → main+hotbar.
    static void PlayerShiftMove(InventoryEntityComponent pinv, int idx) {
        ref var src = ref pinv.Storage[idx];
        if (src.IsEmpty) return;
        if (idx is >= 9 and <= 35)        // main storage → hotbar
            MoveWithinStorage(pinv.Storage, ref src, 0, 9);
        else if (idx is >= 0 and <= 8)    // hotbar → main storage
            MoveWithinStorage(pinv.Storage, ref src, 9, 36);
        else                               // armour / off-hand → main + hotbar
            MoveWithinStorage(pinv.Storage, ref src, 0, 36);
    }

    // Merges src into matching stacks in storage [start,end), then fills the first empty slot; mutates src.
    static void MoveWithinStorage(InventoryComponent storage, ref ItemStack src, int start, int end) {
        for (int i = start; i < end && !src.IsEmpty; i++) {
            ref var dst = ref storage[i];
            if (!dst.IsEmpty && dst.StacksWith(src) && dst.Count < dst.Type!.MaxStackSize) {
                int move = System.Math.Min(src.Count, dst.Type.MaxStackSize - dst.Count);
                dst.Count += move; src.Count -= move; if (src.Count <= 0) src = default;
            }
        }
        for (int i = start; i < end && !src.IsEmpty; i++) {
            ref var dst = ref storage[i];
            if (dst.IsEmpty) { dst = src; src = default; }
        }
    }

    static void LeftClick(ref ItemStack slot, ref ItemStack cursor) {
        if (cursor.IsEmpty) { cursor = slot; slot = default; return; }
        if (slot.IsEmpty) { slot = cursor; cursor = default; return; }
        if (slot.StacksWith(cursor)) {
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
        if (slot.StacksWith(cursor) && slot.Count < slot.Type!.MaxStackSize) {
            slot.Count++; cursor.Count--; if (cursor.Count <= 0) cursor = default;
            return;
        }
        (slot, cursor) = (cursor, slot);
    }

    void ShiftMove(OpenWindow w, InventoryEntityComponent pinv, int windowSlot) {
        if (!TryResolve(w, pinv, windowSlot, out var inv, out var idx)) return;
        ref var src = ref inv[idx];
        if (src.IsEmpty) return;
        // Chest slots (0-26) move to the player area (27-62) and vice versa.
        var (start, end) = windowSlot < ChestSize ? (ChestSize, ChestWindowSlots) : (0, ChestSize);
        MoveInto(w, pinv, ref src, start, end);
    }

    void MoveInto(OpenWindow w, InventoryEntityComponent pinv, ref ItemStack src, int start, int end) {
        for (int ws = start; ws < end && !src.IsEmpty; ws++)         // merge into same type
            if (TryResolve(w, pinv, ws, out var inv, out var idx)) {
                ref var dst = ref inv[idx];
                if (!dst.IsEmpty && dst.StacksWith(src) && dst.Count < dst.Type!.MaxStackSize) {
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
    static void PutIntoInventory(InventoryEntityComponent pinv, ref ItemStack stack) => stack = pinv.Add(stack);

    // ── Slot layout (chest window): 0-26 chest, 27-53 player main(9-35), 54-62 hotbar(0-8) ──
    static bool TryResolve(OpenWindow w, InventoryEntityComponent pinv, int windowSlot, out InventoryComponent inv, out int index) {
        if (windowSlot is >= 0 and < ChestSize) { inv = w.ChestInv; index = windowSlot; return true; }
        if (windowSlot is >= ChestSize and < ChestSize + 27) { inv = pinv.Storage; index = 9 + (windowSlot - ChestSize); return true; } // 27-53 -> storage 9-35
        if (windowSlot is >= ChestSize + 27 and < ChestWindowSlots) { inv = pinv.Storage; index = windowSlot - (ChestSize + 27); return true; } // 54-62 -> storage 0-8
        inv = null!; index = 0; return false;
    }

    // ── Sending ───────────────────────────────────────────────────────────────

    void SendContent(Server server, OpenWindow w, InventoryEntityComponent pinv) {
        var slots = new ItemStack[ChestWindowSlots];
        for (int i = 0; i < ChestWindowSlots; i++)
            slots[i] = TryResolve(w, pinv, i, out var inv, out var idx) ? inv[idx] : default;
        server.NetServer.Send(w.ClientId, new SetContainerContentS2C(w.Id, w.Revision, slots, w.Cursor));
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

    /// <summary>Window id 0.</summary>
    public static ItemStack[] PlayerWindow(InventoryEntityComponent inv) {
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

    /// <summary>Window id 0.</summary>
    public static bool TryPlayerWindowToStorage(int windowSlot, out int storageIndex) {
        switch (windowSlot) {
            case >= 5 and <= 8: storageIndex = 44 - windowSlot; return true;   // 5(Head)->39 .. 8(Feet)->36
            case >= 9 and <= 35: storageIndex = windowSlot; return true;        // main storage
            case >= 36 and <= 44: storageIndex = windowSlot - 36; return true;  // hotbar -> 0-8
            case 45: storageIndex = InventoryEntityComponent.MainSize + InventoryEntityComponent.ArmorSize; return true; // offhand (40)
            default: storageIndex = 0; return false;                            // crafting / outside
        }
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    static InventoryEntityComponent? PlayerInv(Server server, ulong clientId) =>
        server.TryGetPlayer(clientId, out var h) && h.World.Ecs.IsAlive(h.Entity)
            ? h.World.Ecs.Get<InventoryEntityComponent>(h.Entity)
            : null;

    // A click changed the player's inventory — let subscribers (equipment visibility) re-sync.
    static void PublishInventoryChanged(Server server, ulong clientId) {
        if (server.TryGetPlayer(clientId, out var context))
            server.Events.Publish(new PlayerInventoryChanged(context));
    }

    static InventoryComponent ChestInventory(BlockEntity chest) {
        if (chest.TryGet<InventoryComponent>(out var inv)) return inv;
        inv = new InventoryComponent(ChestSize);
        chest.Add(inv);
        return inv;
    }
}
