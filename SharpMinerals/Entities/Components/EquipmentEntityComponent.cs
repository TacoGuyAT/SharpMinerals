using SharpMinerals.Items;

namespace SharpMinerals.Entities.Components;

/// <summary>The equipment a player was last seen wearing/holding by other clients (held, off-hand, four
/// armour pieces). The visibility layer diffs live inventory against this on each <c>InventoryChanged</c> and
/// broadcasts only the changed slots. Slot ordering is owned by the network layer that reads it.</summary>
[Component]
public sealed class EquipmentEntityComponent {
    public const int SlotCount = 6;

    /// <summary>Last-broadcast equipment, indexed by equipment-slot ordinal. Starts all-empty; the first
    /// broadcast (at join) fills it.</summary>
    public readonly ItemStack[] LastSent = new ItemStack[SlotCount];
}
