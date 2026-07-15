using SharpMinerals.Entities.Components;

namespace SharpMinerals.Network.Containers;

/// <summary>A window whose slots are whatever the caller defines – the "programmable" menu type.</summary>
public sealed class CustomWindow : BaseWindow {
    readonly Slot[] slots;
    readonly IEnumerable<int>[] shiftTargets;

    public CustomWindow(ulong clientId, int windowId, Slot[] slots,
                                      IEnumerable<int>[]? shiftTargets = null)
        : base(clientId, windowId) {
        this.slots = slots;
        this.shiftTargets = shiftTargets ?? new IEnumerable<int>[slots.Length];
    }

    public override int SlotCount => slots.Length;
    public override Slot GetSlot(int windowSlot) => slots[windowSlot];
    public override IEnumerable<int> ShiftTargets(int windowSlot) => shiftTargets[windowSlot] ?? Array.Empty<int>();

    // ---- Player inventory window (id 0) ----

    public const int PlayerWindowSlotCount = 46;

    public static CustomWindow ForPlayerInventory(ulong clientId, InventoryEntityComponent inventory) {
        var slots = new Slot[PlayerWindowSlotCount];
        var shifts = new IEnumerable<int>[PlayerWindowSlotCount];

        for(int i = 0; i <= 4; i++)
            slots[i] = Slot.Inert; // crafting grid

        var armorStorage = new[] { 39, 38, 37, 36 }; // head..feet
        for(int i = 0; i < 4; i++)
            slots[5 + i] = StorageSlot(inventory, armorStorage[i]);

        for(int i = 9; i <= 35; i++)
            slots[i] = StorageSlot(inventory, i);
        for(int i = 0; i < 9; i++)
            slots[36 + i] = StorageSlot(inventory, i);
        slots[45] = StorageSlot(inventory, InventoryEntityComponent.MainSize + InventoryEntityComponent.ArmorSize);

        var main = Range(9, 36).ToArray();
        var hotbar = Range(36, 45).ToArray();
        var hotbarThenMain = hotbar.Concat(main).ToArray();

        for(int i = 9; i <= 35; i++)
            shifts[i] = hotbar;
        for(int i = 36; i <= 44; i++)
            shifts[i] = main;
        for(int i = 5; i <= 8; i++)
            shifts[i] = hotbarThenMain;
        shifts[45] = hotbarThenMain;

        return new CustomWindow(clientId, 0, slots, shifts);
    }

    static Slot StorageSlot(InventoryEntityComponent inventory, int storageIndex) => new() {
        // TODO: When player is disconnected this will throw an NRE
        Get = () => inventory.Storage[storageIndex],
        Set = v => { if(inventory is { } pinv) pinv.Storage[storageIndex] = v; }
    };

    static IEnumerable<int> Range(int start, int end) {
        for(int i = start; i < end; i++)
            yield return i;
    }
}