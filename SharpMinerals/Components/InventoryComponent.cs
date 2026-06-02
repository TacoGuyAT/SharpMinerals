using SharpMinerals.Items;

namespace SharpMinerals.Components;

/// <summary>A fixed-size container of <see cref="ItemStack"/>s — the backing storage any inventory is built
/// on. Slot count is set at construction and never changes.</summary>
public class InventoryComponent {
    readonly ItemStack[] slots;

    public InventoryComponent(int size) => slots = new ItemStack[size];

    public int Size => slots.Length;

    /// <summary>Slot access by reference, so callers can mutate a stack in place.</summary>
    public ref ItemStack this[int index] => ref slots[index];

    public bool IsEmpty {
        get {
            foreach (var s in slots)
                if (!s.IsEmpty) return false;
            return true;
        }
    }

    /// <summary>Adds <paramref name="stack"/> into the slot range (whole container by default): merges into
    /// matching stacks, then fills empties, each capped at the item's max stack size. Returns what didn't fit.</summary>
    public ItemStack Add(ItemStack stack, int start = 0, int count = -1) {
        if (stack.IsEmpty) return default;
        int max = System.Math.Max(1, stack.Type!.MaxStackSize);
        int end = count < 0 ? slots.Length : System.Math.Min(start + count, slots.Length);

        // Top up matching, non-full stacks.
        for (int i = start; i < end && stack.Count > 0; i++) {
            ref var dst = ref slots[i];
            if (dst.IsEmpty || dst.Count >= max || !dst.StacksWith(stack)) continue;
            int move = System.Math.Min(stack.Count, max - dst.Count);
            dst.Count += move;
            stack.Count -= move;
        }
        // Fill empty slots.
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

    public void Clear() => Array.Clear(slots);
}
