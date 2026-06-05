using SharpMinerals.Items;
using SharpMinerals.Network.Buffers;
using SharpMinerals.Persistence;

namespace SharpMinerals.Entities.Components;

/// <summary>A dropped item-stack entity: the stack it represents plus its lifetime. Persists the stack and age so
/// items on the ground survive save/load (pickup delay + network id are session-only and reset on load).</summary>
[Component]
public struct PickupEntityComponent : IPersistentComponent {
    public ItemStack Stack;
    /// <summary>Ticks since the item was dropped.</summary>
    public int Age;
    /// <summary>Ticks before the item can be picked up.</summary>
    public int PickupDelay;
    /// <summary>Network entity id assigned when announced to clients (0 = not yet announced).</summary>
    public int EntityId;

    public readonly void Write(MinecraftStream s) {
        StackCodec.Write(s, Stack);
        s.WriteVarInt(Age);
    }

    public static PickupEntityComponent Read(MinecraftStream s) =>
        new() { Stack = StackCodec.Read(s), Age = s.ReadVarInt() };
}
