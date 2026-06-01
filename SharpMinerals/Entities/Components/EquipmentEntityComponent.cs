using SharpMinerals.Items;

namespace SharpMinerals.Entities.Components;

/// <summary>
/// The equipment a player was last seen wearing/holding by OTHER clients — held item, off-hand, and the
/// four armour pieces, in <c>EquipmentSlot</c> order. The visibility layer diffs the player's live
/// inventory against this on every <c>InventoryChanged</c> and broadcasts only the slots that changed,
/// then updates it. Opaque storage (six <see cref="ItemStack"/>s); the slot ordering is owned by the
/// network layer that reads it.
/// </summary>
public sealed class EquipmentEntityComponent {
    public const int SlotCount = 6;

    /// <summary>Last-broadcast equipment, indexed by equipment-slot ordinal. Starts all-empty; the first
    /// broadcast (at join) fills it.</summary>
    public readonly ItemStack[] LastSent = new ItemStack[SlotCount];
}
