using SharpMinerals.Items;

namespace SharpMinerals.Components;

/// <summary>
/// A fixed-size container of <see cref="ItemStack"/>s — the backing storage any
/// inventory is built on (a player, a chest, …). Concrete and reusable; the slot
/// count is set at construction and never changes.
/// </summary>
public class Inventory {
    readonly ItemStack[] slots;

    public Inventory(int size) => slots = new ItemStack[size];

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

    /// <summary>Empties every slot.</summary>
    public void Clear() => Array.Clear(slots);
}
