using SharpMinerals.Items;
using SharpMinerals.Network.Buffers;
using SharpMinerals.Network.Messages;
using SharpMinerals.Network.Nbt;

namespace SharpMinerals.Network.Protocols.JE762.Codecs;

// Shared slot helper: encode our ItemStack as a wire Slot (empty stacks write "not present").
internal static class SlotWire {
    public static void WriteStack(MinecraftStream s, ItemStack stack) {
        if (stack.IsEmpty) {
            s.WriteEmptySlot();
            return;
        }
        int id = s.Types!.ItemId(stack); // stack-aware (wool colour, ...)

        // A mod-added type renders as the fallback item; attach NBT (custom name + identity marker) so the
        // client tells it apart and won't stack it with the fallback or other customs. Vanilla types write a plain slot.
        if (stack.Type!.IsCustom) {
            s.WriteBool(true);
            s.WriteVarInt(id);
            s.WriteByte2((sbyte)stack.Count);
            CustomItemNbt(stack.Type!).WriteRoot(s, network: false);
        } else {
            s.WriteSlot(id, stack.Count);
        }
    }

    /// <summary>Reads a wire Slot back into our <see cref="ItemStack"/> (inverse of <see cref="WriteStack"/>),
    /// preferring the custom-type NBT marker. Not-present -> empty stack (clear); a present unmappable item -> null.</summary>
    public static ItemStack? ReadStack(MinecraftStream s) {
        if (!s.ReadBool())
            return default(ItemStack); // not present - a deliberately empty slot
        int id = s.ReadVarInt();
        int count = s.ReadByte2();
        var nbt = NbtReader.ReadItemNbt(s);
        if (nbt?.Children.GetValueOrDefault(CustomTypeKey) is NbtString marker && ItemType.TryFromPath(marker.Value, out var custom))
            return new ItemStack(custom, count);
        var stack = s.Types!.FromVanillaItem(id); // resolves colour/state too (e.g. coloured wool)
        if (stack.IsEmpty)
            return null; // present on the wire, but no SharpMinerals type maps to this id
        stack.Count = count;
        return stack;
    }

    /// <summary>NBT key carrying a custom type's registry name: the stacking discriminator and the marker the server reads back.</summary>
    public const string CustomTypeKey = "SharpMineralsType";

    // NBT giving a fallback-rendered custom type a distinct identity on the client.
    static NbtCompound CustomItemNbt(ItemType type) {
        var display = new NbtCompound().Put("Name", CustomNameJson(type));
        return new NbtCompound()
            .Put("display", display)
            // Distinct per custom type -> the client won't merge customs/fallback into one stack, and the
            // server recovers the exact type (its full namespaced id) when the client sends the slot back.
            .Put(CustomTypeKey, type.Id.Full);
    }

    // A 1.20.1 item name is a JSON chat component. Use a translatable keyed by the namespaced id with a humanised fallback.
    static string CustomNameJson(ItemType type) {
        string fallback = Humanize(type.Id.Name);
        return $"{{\"translate\":\"item.{Escape(type.Id.Namespace)}.{Escape(type.Id.Name)}\",\"fallback\":\"{Escape(fallback)}\",\"italic\":false}}";
    }

    static string Humanize(string name) =>
        string.Join(' ', name.Split('_', StringSplitOptions.RemoveEmptyEntries)
            .Select(w => char.ToUpperInvariant(w[0]) + w[1..]));

    static string Escape(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");
}

// -- Clientbound ---------------------------------------------------------------

internal sealed class OpenScreenS2CCodec : ICodec<OpenScreenS2C> {
    public void Encode(MinecraftStream s, OpenScreenS2C m) {
        s.WriteVarInt(m.WindowId);
        s.WriteVarInt(m.WindowType);
        s.WriteString("{\"text\":\"" + m.Title + "\"}"); // Chat (JSON)
    }

    public OpenScreenS2C Decode(MinecraftStream s) =>
        throw new NotSupportedException("OpenScreenS2C is clientbound only.");
}

internal sealed class SetContainerContentS2CCodec : ICodec<SetContainerContentS2C> {
    public void Encode(MinecraftStream s, SetContainerContentS2C m) {
        s.WriteUByte((byte)m.WindowId);
        s.WriteVarInt(m.Revision);
        s.WriteVarInt(m.Slots.Count);
        foreach (var stack in m.Slots) SlotWire.WriteStack(s, stack);
        SlotWire.WriteStack(s, m.Carried);
    }

    public SetContainerContentS2C Decode(MinecraftStream s) =>
        throw new NotSupportedException("SetContainerContentS2C is clientbound only.");
}

internal sealed class SetContainerSlotS2CCodec : ICodec<SetContainerSlotS2C> {
    public void Encode(MinecraftStream s, SetContainerSlotS2C m) {
        s.WriteByte2((sbyte)m.WindowId);  // signed: -1/-2 are special windows
        s.WriteVarInt(m.Revision);
        s.WriteShort((short)m.Slot);
        SlotWire.WriteStack(s, m.Data);
    }

    public SetContainerSlotS2C Decode(MinecraftStream s) =>
        throw new NotSupportedException("SetContainerSlotS2C is clientbound only.");
}

internal sealed class CloseContainerS2CCodec : ICodec<CloseContainerS2C> {
    public void Encode(MinecraftStream s, CloseContainerS2C m) => s.WriteUByte((byte)m.WindowId);
    public CloseContainerS2C Decode(MinecraftStream s) =>
        throw new NotSupportedException("CloseContainerS2C is clientbound only.");
}

internal sealed class SetHeldItemS2CCodec : ICodec<SetHeldItemS2C> {
    public void Encode(MinecraftStream s, SetHeldItemS2C m) => s.WriteByte2((sbyte)m.Slot);
    public SetHeldItemS2C Decode(MinecraftStream s) =>
        throw new NotSupportedException("SetHeldItemS2C is clientbound only.");
}

// -- Serverbound ---------------------------------------------------------------

internal sealed class ClickContainerC2SCodec : ICodec<ClickContainerC2S> {
    public void Encode(MinecraftStream s, ClickContainerC2S m) =>
        throw new NotSupportedException("ClickContainerC2S is serverbound only.");

    public ClickContainerC2S Decode(MinecraftStream s) {
        int window = s.ReadUByte();
        int revision = s.ReadVarInt();
        int slot = s.ReadShort();
        int button = s.ReadByte2();
        int mode = s.ReadVarInt();
        // The changed-slot array + carried item follow; left unread (server authoritative).
        return new ClickContainerC2S(window, revision, slot, button, mode);
    }
}

internal sealed class CloseContainerC2SCodec : ICodec<CloseContainerC2S> {
    public void Encode(MinecraftStream s, CloseContainerC2S m) =>
        throw new NotSupportedException("CloseContainerC2S is serverbound only.");

    public CloseContainerC2S Decode(MinecraftStream s) => new(s.ReadUByte());
}

internal sealed class SetHeldItemC2SCodec : ICodec<SetHeldItemC2S> {
    public void Encode(MinecraftStream s, SetHeldItemC2S m) =>
        throw new NotSupportedException("SetHeldItemC2S is serverbound only.");

    public SetHeldItemC2S Decode(MinecraftStream s) => new(s.ReadShort());
}

internal sealed class SetCreativeModeSlotC2SCodec : ICodec<SetCreativeModeSlotC2S> {
    public void Encode(MinecraftStream s, SetCreativeModeSlotC2S m) =>
        throw new NotSupportedException("SetCreativeModeSlotC2S is serverbound only.");

    public SetCreativeModeSlotC2S Decode(MinecraftStream s) {
        int slot = s.ReadShort();
        return new SetCreativeModeSlotC2S(slot, SlotWire.ReadStack(s)); // null Stack = an item we can't represent
    }
}
