using SharpMinerals.Items;
using SharpMinerals.Network.Buffers;
using SharpMinerals.Persistence;

namespace SharpMinerals.Components;

/// <summary>A fixed-size container of <see cref="ItemStack"/>s - the backing storage any inventory is built
/// on. Slot count is set at construction and never changes. Persists itself (slot count + each stack) so a block
/// entity's contents survive save/load via the <see cref="ComponentObject"/> bag.</summary>
[Component]
public class InventoryComponent : IPersistentComponent {
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

    /// <summary>Removes up to <paramref name="amount"/> items from slot <paramref name="index"/>, clearing the slot
    /// when it empties. Returns the number actually removed (0 if the slot was already empty).</summary>
    public int Consume(int index, int amount = 1) {
        ref var slot = ref slots[index];
        if (slot.IsEmpty || amount <= 0) return 0;
        int removed = System.Math.Min(amount, slot.Count);
        slot.Count -= removed;
        if (slot.Count <= 0) slot = default;
        return removed;
    }

    public void Clear() => Array.Clear(slots);

    public void Write(MinecraftStream s) {
        s.WriteVarInt(slots.Length);
        foreach (var stack in slots) StackCodec.Write(s, stack);
    }

    public static InventoryComponent Read(MinecraftStream s) {
        var inv = new InventoryComponent(s.ReadVarInt());
        for (int i = 0; i < inv.slots.Length; i++) inv.slots[i] = StackCodec.Read(s);
        return inv;
    }
}
