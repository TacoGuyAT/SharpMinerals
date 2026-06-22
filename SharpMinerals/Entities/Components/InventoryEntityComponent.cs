using SharpMinerals.Items;
using SharpMinerals.Network.Buffers;

namespace SharpMinerals.Entities.Components;

/// <summary>An entity's inventory layout over a backing <see cref="InventoryComponent"/>: 36 main slots
/// (hotbar 0-8 + storage 9-35), 4 armor slots, and an off-hand. The held item is the selected hotbar slot.
/// Persists itself (selected slot + the backing storage) so a saved entity keeps its inventory and held item.</summary>
[Component]
public sealed class InventoryEntityComponent : IPersistentComponent {
    public const int MainSize = 36;
    public const int HotbarSize = 9;
    public const int ArmorSize = 4;
    public const int OffhandSize = 1;
    public const int CraftingSize = 5;
    public const int TotalSize = MainSize + ArmorSize + OffhandSize + CraftingSize; // 46

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

    public ref ItemStack Held => ref Storage[SelectedSlot];

    /// <summary>A main slot (0-8 hotbar, 9-35 storage).</summary>
    public ref ItemStack Main(int index) => ref Storage[index];

    public ref ItemStack Armor(ArmorSlot slot) => ref Storage[ArmorStart + (int)slot];

    public ref ItemStack Offhand => ref Storage[OffhandStart];

    /// <summary>Adds a stack to the main inventory (slots 0-35; armor and off-hand are never auto-filled)
    /// and returns whatever didn't fit.</summary>
    public ItemStack Add(ItemStack stack) => Storage.Add(stack, 0, MainSize);

    /// <summary>Consumes up to <paramref name="amount"/> items from the held (selected hotbar) slot; returns the
    /// number actually removed.</summary>
    public int ConsumeHeld(int amount = 1) => Storage.Consume(SelectedSlot, amount);

    public void Write(MinecraftStream s) {
        s.WriteVarInt(SelectedSlot);
        Storage.Write(s);
    }

    public static InventoryEntityComponent Read(MinecraftStream s) {
        int selected = s.ReadVarInt();
        var storage = InventoryComponent.Read(s);
        return new InventoryEntityComponent(storage) { SelectedSlot = selected };
    }
}

/// <summary>The four armor slots, ordered foot-to-head as in the vanilla equipment array.</summary>
public enum ArmorSlot { Feet, Legs, Chest, Head }
