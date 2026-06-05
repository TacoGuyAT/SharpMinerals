using SharpMinerals.Components;
using SharpMinerals.Items;
using SharpMinerals.Network.Buffers;

namespace SharpMinerals.Persistence;

/// <summary>Shared <see cref="ItemStack"/> <-> bytes for the persistence codecs. Items are written by registry
/// NAME (not flyweight identity) so a stack resolves to the same definition on load even as internal ids shift; its
/// per-instance <see cref="ItemStack.Data"/> (carried block state, future NBT-like components) goes through the
/// length-prefixed <see cref="ComponentBag"/>, the same host every block entity uses.</summary>
internal static class StackCodec {
    public static void Write(MinecraftStream s, ItemStack stack) {
        if (stack.IsEmpty) { s.WriteBool(false); return; }
        s.WriteBool(true);
        s.WriteString(stack.Type!.Id.Full);
        s.WriteVarInt(stack.Count);
        ComponentBag.Write(s, stack.Data); // carried block state (a wool's colour) + any other persistent data; null = empty bag
    }

    public static ItemStack Read(MinecraftStream s) {
        if (!s.ReadBool()) return default;
        var type = Resolve(s.ReadString());
        var stack = new ItemStack(type, s.ReadVarInt());

        var data = new ComponentObject();
        ComponentBag.Read(s, data);
        if (data.Components.Any()) stack.Data = data; // only attach a bag that actually carried something
        return stack;
    }

    static ItemType Resolve(string name) =>
        ItemRegistry.FromName(name)
            ?? throw new InvalidDataException($"Unknown item '{name}' in saved data.");
}
