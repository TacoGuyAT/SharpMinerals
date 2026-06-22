using SharpMinerals.Components;
using SharpMinerals.Entities.Components;

namespace SharpMinerals.Network.Containers;

/// <summary>A window over any single <see cref="InventoryComponent"/> - a chest, barrel, ender chest,
/// or anything else backed by one - plus the viewing player's own main storage + hotbar, appended.</summary>
public sealed class InventoryWindow : BaseWindow {
    const int PlayerAreaSize = InventoryEntityComponent.MainSize;

    readonly InventoryComponent backing;
    readonly int size;
    readonly Func<InventoryEntityComponent?> resolvePinv;

    public InventoryWindow(ulong clientId, int windowId, InventoryComponent backing, int size,
                                     Func<InventoryEntityComponent?> resolvePinv)
        : base(clientId, windowId) {
        this.backing = backing;
        this.size = size;
        this.resolvePinv = resolvePinv;
    }

    public override int SlotCount => size + PlayerAreaSize;

    public override Slot GetSlot(int windowSlot) {
        if(windowSlot < size)
            return Slot.Backing(backing, windowSlot);
        int rel = windowSlot - size;
        int storageIndex = rel < 27 ? 9 + rel : rel - 27;
        return new Slot {
            Get = () => resolvePinv()?.Storage[storageIndex] ?? default,
            Set = v => { if(resolvePinv() is { } pinv) pinv.Storage[storageIndex] = v; }
        };
    }

    public override IEnumerable<int> ShiftTargets(int windowSlot) =>
        windowSlot < size ? Range(size, SlotCount) : Range(0, size);

    static IEnumerable<int> Range(int start, int end) {
        for(int i = start; i < end; i++)
            yield return i;
    }
}