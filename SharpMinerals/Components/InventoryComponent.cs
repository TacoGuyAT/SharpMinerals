using SharpMinerals.Items;

namespace SharpMinerals.Components;

/// <summary>
/// A fixed-size container of <see cref="ItemStack"/>s — the backing storage any
/// inventory is built on (a player, a chest, …). Concrete and reusable; the slot
/// count is set at construction and never changes.
/// </summary>
public class InventoryComponent {
    readonly ItemStack[] slots;

    public InventoryComponent(int size) => slots = new ItemStack[size];

    /// <summary>Number of slots.</summary>
    public int Size => slots.Length;

    /// <summary>Slot access by reference, so callers can mutate a stack in place.</summary>
    public ref ItemStack this[int index] => ref slots[index];

    /// <summary>True when every slot is empty.</summary>
    public bool IsEmpty {
        get {
            foreach (var s in slots)
                if (!s.IsEmpty) return false;
            return true;
        }
    }

    /// <summary>
    /// Adds <paramref name="stack"/> into the slots <c>[<paramref name="start"/>, start + <paramref name="count"/>)</c>
    /// (the whole container by default): first merging into matching stacks that still have room, then filling
    /// empty slots — each slot capped at the item's max stack size, so a large count spreads across several
    /// slots. Returns whatever didn't fit (empty if it all did).
    /// </summary>
    public ItemStack Add(ItemStack stack, int start = 0, int count = -1) {
        if (stack.IsEmpty) return default;
        int max = System.Math.Max(1, stack.Type!.MaxStackSize);
        int end = count < 0 ? slots.Length : System.Math.Min(start + count, slots.Length);

        // 1) Top up matching, non-full stacks.
        for (int i = start; i < end && stack.Count > 0; i++) {
            ref var dst = ref slots[i];
            if (dst.IsEmpty || dst.Count >= max || !dst.StacksWith(stack)) continue;
            int move = System.Math.Min(stack.Count, max - dst.Count);
            dst.Count += move;
            stack.Count -= move;
        }
        // 2) Fill empty slots, at most one max stack each.
        for (int i = start; i < end && stack.Count > 0; i++) {
            ref var dst = ref slots[i];
            if (!dst.IsEmpty) continue;
            int move = System.Math.Min(stack.Count, max);
            dst = stack;
            dst.Count = move;
            stack.Count -= move;
        }
        return stack.Count > 0 ? stack : default;
    }

    /// <summary>Empties every slot.</summary>
    public void Clear() => Array.Clear(slots);
}
