using SharpMinerals.Blocks;
using SharpMinerals.Blocks.Descriptors;
using SharpMinerals.Items;
using SharpMinerals.Network.Buffers;

namespace SharpMinerals.Persistence;

/// <summary>
/// Shared <see cref="ItemStack"/> ⇆ bytes used by the persistence codecs (player inventory and
/// chest contents). Items are written by registry NAME — not flyweight identity — and any
/// carried block state by its property values, so a stack resolves to the same definition on
/// load even as internal ids shift.
/// </summary>
internal static class StackCodec {
    public static void Write(MinecraftStream s, ItemStack stack) {
        if (stack.IsEmpty) { s.WriteBool(false); return; }
        s.WriteBool(true);
        s.WriteString(stack.Type!.Name);
        s.WriteVarInt(stack.Count);

        // Carried block state (e.g. a wool's colour): the value of each of the type's properties.
        if (stack.Type is BlockType bt && bt.TryGet<StatesBlockDescriptor>(out var sp) && stack.State is { } bs) {
            s.WriteBool(true);
            foreach (var property in sp.States) s.WriteVarInt(bs.Get(property));
        } else {
            s.WriteBool(false);
        }
    }

    public static ItemStack Read(MinecraftStream s) {
        if (!s.ReadBool()) return default;
        var type = Resolve(s.ReadString());
        var stack = new ItemStack(type, s.ReadVarInt());

        // The flag matches what Write emitted: state is present iff the type is a stateful block,
        // so the same definition consumes exactly the values written.
        if (s.ReadBool() && type is BlockType bt && bt.TryGet<StatesBlockDescriptor>(out var sp)) {
            var bs = new BlockState(bt);
            foreach (var property in sp.States) bs.Set(property, s.ReadVarInt());
            stack = stack.WithState(bs);
        }
        return stack;
    }

    static ItemType Resolve(string name) =>
        BlockRegistry.FromName(name) ?? ItemRegistry.FromName(name)
            ?? throw new InvalidDataException($"Unknown item '{name}' in saved data.");
}
