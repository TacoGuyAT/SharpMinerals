using SharpMinerals.Entities.Components;
using SharpMinerals.Items;
using SharpMinerals.Network.Messages;

namespace SharpMinerals.Network.Containers;
using Math = System.Math;

/// <summary>Base for a single open window: per-window click application (left/right/shift/drag),
/// cursor + revision bookkeeping, and the snapshot used to resync the client.</summary>
public abstract class BaseWindow {
    public const int OutsideSlot = -999;
    const int OffhandButton = 40;

    public readonly ulong ClientId;
    public readonly int WindowId;
    public int Revision { get; private set; }
    public ItemStack Cursor; // per‑handler cursor (shared across all open windows for this client is not enforced – each handler has its own)

    List<int>? dragPaint;

    protected BaseWindow(ulong clientId, int windowId) {
        ClientId = clientId;
        WindowId = windowId;
        Cursor = default;
    }

    public abstract int SlotCount { get; }
    public abstract Slot GetSlot(int windowSlot);

    /// <summary>Window slots a shift-click from <paramref name="windowSlot"/> should try, in order.</summary>
    public virtual IEnumerable<int> ShiftTargets(int windowSlot) => Array.Empty<int>();

    public void MarkDirty() => Revision++;

    public ItemStack[] BuildSnapshot() {
        var slots = new ItemStack[SlotCount];
        for(int i = 0; i < SlotCount; i++)
            slots[i] = GetSlot(i).Get();
        return slots;
    }

    /// <summary>Applies one click message. Returns true if the client should resync (all clicks except drag start/add).</summary>
    public bool HandleClick(Server server, InventoryEntityComponent pinv, ClickContainerC2S msg) {
        if(msg.Mode == 5)
            return HandleDrag(msg);

        if(msg.Slot == OutsideSlot) {
            if(msg.Mode == 0)
                DropCursor(server, whole: msg.Button == 0);
            return true;
        }
        if(msg.Slot < 0 || msg.Slot >= SlotCount)
            return true;

        var slot = GetSlot(msg.Slot);
        if(slot.Locked)
            return true;

        var slotStack = slot.Get();
        var cursor = Cursor;

        // Intercept
        if(slot.Intercept != null) {
            var (handled, newSlot, newCursor) = slot.Intercept(msg, slotStack, cursor);
            if(handled) {
                slot.Set(newSlot);
                Cursor = newCursor;
                return true;
            }
            // If not handled, continue with the possibly modified slot/cursor
            slotStack = newSlot;
            cursor = newCursor;
        }

        // Default behaviour
        if(msg.Mode == 1) {
            ShiftMove(msg.Slot, ref slotStack);
        } else {
            Dispatch(server, pinv, msg, ref slotStack, ref cursor);
            Cursor = cursor;
        }
        slot.Set(slotStack);
        return true;
    }

    // ---- Click kinds ----

    void Dispatch(Server server, InventoryEntityComponent pinv, ClickContainerC2S msg,
                  ref ItemStack slotStack, ref ItemStack cursor) {
        switch(msg.Mode) {
            case 0 when msg.Button == 0:
                LeftClick(ref slotStack, ref cursor);
                break;
            case 0 when msg.Button == 1:
                RightClick(ref slotStack, ref cursor);
                break;
            case 2 when HotbarSwapTarget(msg.Button, out var hb):
                SwapWithStorage(ref slotStack, pinv, hb);
                break;
            case 3 when !slotStack.IsEmpty && cursor.IsEmpty:
                cursor = CloneFull(slotStack);
                break;
            case 4:
                DropFromSlot(server, ref slotStack, whole: msg.Button == 1);
                break;
        }
    }

    static void LeftClick(ref ItemStack slot, ref ItemStack cursor) {
        if(cursor.IsEmpty) { cursor = slot; slot = default; return; }
        if(slot.IsEmpty) { slot = cursor; cursor = default; return; }
        if(slot.StacksWith(cursor)) {
            int move = Math.Min(cursor.Count, slot.Type!.MaxStackSize - slot.Count);
            if(move > 0) {
                slot.Count += move;
                cursor.Count -= move;
                if(cursor.Count <= 0)
                    cursor = default;
                return;
            }
        }
        (slot, cursor) = (cursor, slot);
    }

    static void RightClick(ref ItemStack slot, ref ItemStack cursor) {
        if(cursor.IsEmpty) {
            if(slot.IsEmpty)
                return;
            int half = (slot.Count + 1) / 2;
            cursor = new ItemStack(slot.Type!, half) { Data = slot.Data };
            slot.Count -= half;
            if(slot.Count <= 0)
                slot = default;
            return;
        }
        if(slot.IsEmpty) {
            slot = new ItemStack(cursor.Type!, 1) { Data = cursor.Data };
            cursor.Count--;
            if(cursor.Count <= 0)
                cursor = default;
            return;
        }
        if(slot.StacksWith(cursor) && slot.Count < slot.Type!.MaxStackSize) {
            slot.Count++;
            cursor.Count--;
            if(cursor.Count <= 0)
                cursor = default;
            return;
        }
        (slot, cursor) = (cursor, slot);
    }

    static void SwapWithStorage(ref ItemStack slotStack, InventoryEntityComponent pinv, int storageIndex) {
        var b = pinv.Storage[storageIndex];
        pinv.Storage[storageIndex] = slotStack;
        slotStack = b;
    }

    static ItemStack CloneFull(ItemStack item) {
        var full = item;
        full.Count = item.Type!.MaxStackSize;
        return full;
    }

    void DropFromSlot(Server server, ref ItemStack slotStack, bool whole) {
        if(slotStack.IsEmpty)
            return;
        var dropped = slotStack;
        if(whole || slotStack.Count <= 1)
            slotStack = default;
        else { dropped.Count = 1; slotStack.Count -= 1; }
        Toss(server, dropped);
    }

    void DropCursor(Server server, bool whole) {
        if(Cursor.IsEmpty)
            return;
        var dropped = Cursor;
        if(whole || Cursor.Count <= 1)
            Cursor = default;
        else { dropped.Count = 1; Cursor.Count -= 1; }
        Toss(server, dropped);
    }

    void Toss(Server server, ItemStack stack) {
        if(server.TryGetPlayer(ClientId, out var ctx) && ctx.World.Ecs.IsAlive(ctx.Entity))
            ctx.World.TossItem(ctx.World.Ecs.Get<TransformEntityComponent>(ctx.Entity), stack);
    }

    static bool HotbarSwapTarget(int button, out int storageIndex) {
        if(button is >= 0 and <= 8) { storageIndex = button; return true; }
        if(button == OffhandButton) { storageIndex = InventoryEntityComponent.MainSize + InventoryEntityComponent.ArmorSize; return true; }
        storageIndex = 0;
        return false;
    }

    // ---- Shift-click ----

    void ShiftMove(int windowSlot, ref ItemStack src) {
        if(src.IsEmpty)
            return;
        MoveInto(ref src, ShiftTargets(windowSlot));
    }

    void MoveInto(ref ItemStack src, IEnumerable<int> targets) {
        var list = targets as IReadOnlyList<int> ?? targets.ToList();
        foreach(int ws in list) {
            if(src.IsEmpty)
                break;
            var dst = GetSlot(ws);
            if(dst.Locked)
                continue;
            var d = dst.Get();
            if(d.IsEmpty || !d.StacksWith(src) || d.Count >= d.Type!.MaxStackSize)
                continue;
            int move = Math.Min(src.Count, d.Type.MaxStackSize - d.Count);
            d.Count += move;
            src.Count -= move;
            if(src.Count <= 0)
                src = default;
            dst.Set(d);
        }
        foreach(int ws in list) {
            if(src.IsEmpty)
                break;
            var dst = GetSlot(ws);
            if(dst.Locked)
                continue;
            if(!dst.Get().IsEmpty)
                continue;
            dst.Set(src);
            src = default;
        }
    }

    // ---- Drag (mode 5) ----

    bool HandleDrag(ClickContainerC2S msg) {
        switch(msg.Button) {
            case 0 or 4 or 8:
                dragPaint = new();
                return false;
            case 1 or 5 or 9:
                if(msg.Slot != OutsideSlot && dragPaint is { } p && !p.Contains(msg.Slot))
                    p.Add(msg.Slot);
                return false;
            case 2 or 6 or 10:
                if(dragPaint is { } painted) { DistributeDrag(msg.Button, painted); dragPaint = null; }
                return true;
            default:
                return false;
        }
    }

    void DistributeDrag(int endButton, List<int> windowSlots) {
        if(Cursor.IsEmpty)
            return;
        var targets = windowSlots.Where(s => s >= 0 && s < SlotCount && !GetSlot(s).Locked).ToList();
        if(targets.Count == 0)
            return;

        var type = Cursor.Type!;
        int maxStack = type.MaxStackSize;

        if(endButton == 10) {
            foreach(int ws in targets) {
                var slot = GetSlot(ws);
                if(slot.Get().IsEmpty)
                    slot.Set(new ItemStack(type, maxStack) { Data = Cursor.Data });
            }
            return;
        }

        int total = Cursor.Count;
        int perSlot = endButton == 2 ? total / targets.Count : 1;
        int placed = 0;
        foreach(int ws in targets) {
            if(placed >= total || perSlot <= 0)
                break;
            var slot = GetSlot(ws);
            var s = slot.Get();
            int give;
            if(s.IsEmpty) {
                give = Math.Min(perSlot, Math.Min(maxStack, total - placed));
                if(give <= 0)
                    continue;
                s = new ItemStack(type, give) { Data = Cursor.Data };
            } else if(s.StacksWith(Cursor) && s.Count < maxStack) {
                give = Math.Min(perSlot, Math.Min(maxStack - s.Count, total - placed));
                if(give <= 0)
                    continue;
                s.Count += give;
            } else
                continue;
            slot.Set(s);
            placed += give;
        }
        Cursor.Count = total - placed;
        if(Cursor.Count <= 0)
            Cursor = default;
    }
}