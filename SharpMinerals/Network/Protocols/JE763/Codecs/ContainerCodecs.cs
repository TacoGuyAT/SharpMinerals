using SharpMinerals.Items;
using SharpMinerals.Network.Buffers;
using SharpMinerals.Network.Messages;

namespace SharpMinerals.Network.Protocols.JE763.Codecs;

// Shared slot helper: encode one of our ItemStacks as a wire Slot, mapping the item
// type to its vanilla id. Empty stacks write a "not present" slot.
internal static class SlotWire {
    public static void WriteStack(MinecraftStream s, ItemStack stack) {
        if (stack.IsEmpty)
            s.WriteEmptySlot();
        else
            s.WriteSlot(TypeMapper.ItemId(stack.Type!), stack.Count);
    }
}

// ── Clientbound ───────────────────────────────────────────────────────────────

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

// ── Serverbound ───────────────────────────────────────────────────────────────

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
        var item = s.ReadSlotLite();
        return new SetCreativeModeSlotC2S(slot, item?.ItemId, item?.Count ?? 0);
    }
}
