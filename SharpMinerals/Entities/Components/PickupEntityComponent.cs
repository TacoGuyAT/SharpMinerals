using SharpMinerals.Items;

namespace SharpMinerals.Entities.Components;

/// <summary>A dropped item-stack entity: the stack it represents plus its lifetime.</summary>
public struct PickupEntityComponent {
    public ItemStack Stack;
    /// <summary>Ticks since the item was dropped.</summary>
    public int Age;
    /// <summary>Ticks before the item can be picked up.</summary>
    public int PickupDelay;
    /// <summary>Network entity id assigned when announced to clients (0 = not yet announced).</summary>
    public int EntityId;
}
