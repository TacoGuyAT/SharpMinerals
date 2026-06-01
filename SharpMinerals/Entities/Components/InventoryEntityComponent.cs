using SharpMinerals.Components;
using SharpMinerals.Items;

namespace SharpMinerals.Entities.Components;

/// <summary>
/// An entity's inventory layout over a backing <see cref="InventoryComponent"/>: 36 main slots
/// (hotbar 0-8 + storage 9-35), 4 armor slots, and an off-hand ("second hand"). The
/// held item is the selected hotbar slot. This is the ECS component an entity carries;
/// it references its backing storage rather than owning raw slot arrays.
/// </summary>
public sealed class InventoryEntityComponent {
    public const int MainSize = 36;
    public const int HotbarSize = 9;
    public const int ArmorSize = 4;
    public const int OffhandSize = 1;
    public const int TotalSize = MainSize + ArmorSize + OffhandSize; // 41

    // Slot ranges within the backing storage.
    const int ArmorStart = MainSize;            // 36..39
    const int OffhandStart = MainSize + ArmorSize; // 40

    /// <summary>The backing storage: [0-35] main (hotbar 0-8 + storage 9-35), [36-39] armor, [40] off-hand.</summary>
    public InventoryComponent Storage { get; }

    /// <summary>The selected hotbar slot (0-8); identifies the held item.</summary>
    public int SelectedSlot;

    /// <summary>Creates an inventory over its own freshly-allocated storage.</summary>
    public InventoryEntityComponent() : this(new InventoryComponent(TotalSize)) { }

    /// <summary>Creates an inventory over existing backing storage (must hold at least <see cref="TotalSize"/> slots).</summary>
    public InventoryEntityComponent(InventoryComponent storage) {
        if (storage.Size < TotalSize)
            throw new ArgumentException($"Entity inventory needs at least {TotalSize} slots, got {storage.Size}.", nameof(storage));
        Storage = storage;
    }

    /// <summary>The currently held stack — the selected hotbar slot.</summary>
    public ref ItemStack Held => ref Storage[SelectedSlot];

    /// <summary>A main slot (0-8 hotbar, 9-35 storage).</summary>
    public ref ItemStack Main(int index) => ref Storage[index];

    /// <summary>An armor slot.</summary>
    public ref ItemStack Armor(ArmorSlot slot) => ref Storage[ArmorStart + (int)slot];

    /// <summary>The off-hand ("second hand") slot.</summary>
    public ref ItemStack Offhand => ref Storage[OffhandStart];

    /// <summary>
    /// Adds a stack to the main inventory (merging into matching stacks, then filling
    /// empty slots) and returns whatever did not fit.
    /// </summary>
    public ItemStack Add(ItemStack stack) {
        for (int i = 0; i < MainSize && !stack.IsEmpty; i++) {
            ref var dst = ref Storage[i];
            if (!dst.IsEmpty && dst.StacksWith(stack) && dst.Count < dst.Type!.MaxStackSize) {
                int move = System.Math.Min(stack.Count, dst.Type.MaxStackSize - dst.Count);
                dst.Count += move; stack.Count -= move; if (stack.Count <= 0) stack = default;
            }
        }
        for (int i = 0; i < MainSize && !stack.IsEmpty; i++) {
            ref var dst = ref Storage[i];
            if (dst.IsEmpty) { dst = stack; stack = default; }
        }
        return stack;
    }
}

/// <summary>The four armor slots, ordered foot-to-head as in the vanilla equipment array.</summary>
public enum ArmorSlot { Feet, Legs, Chest, Head }
