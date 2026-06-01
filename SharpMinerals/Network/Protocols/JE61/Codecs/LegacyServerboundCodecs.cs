using SharpMinerals.Math;
using SharpMinerals.Network.Buffers;
using SharpMinerals.Network.Messages;

namespace SharpMinerals.Network.Protocols.JE61.Codecs;

// EVERY serverbound 1.5.2 packet is decoded here so the length-prefix-free stream can never desync on
// an unhandled packet. Most are decoded-and-ignored for now; wire them to game logic as features land.
// All are serverbound-only (Encode throws). Item-bearing packets consume the Slot via SkipLegacySlot.

static class LegacySb {
    public static NotSupportedException Outbound(string name) => new($"{name} is serverbound only.");
}

/// <summary>Maps the GENERIC <see cref="KeepAliveC2S"/> to the legacy 0x00 int form.</summary>
internal sealed class LegacyKeepAliveC2SMapper : ICodec<KeepAliveC2S> {
    public void Encode(MinecraftStream s, KeepAliveC2S m) => throw LegacySb.Outbound(nameof(KeepAliveC2S));
    public KeepAliveC2S Decode(MinecraftStream s) => new(s.ReadInt());
}

internal sealed class LegacyUseEntityC2SCodec : ICodec<LegacyUseEntityC2S> {
    public void Encode(MinecraftStream s, LegacyUseEntityC2S m) => throw LegacySb.Outbound(nameof(LegacyUseEntityC2S));
    public LegacyUseEntityC2S Decode(MinecraftStream s) => new(s.ReadInt(), s.ReadInt(), s.ReadBool());
}

internal sealed class LegacyPlayerC2SCodec : ICodec<LegacyPlayerC2S> {
    public void Encode(MinecraftStream s, LegacyPlayerC2S m) => throw LegacySb.Outbound(nameof(LegacyPlayerC2S));
    public LegacyPlayerC2S Decode(MinecraftStream s) => new(s.ReadBool());
}

// Movement/digging decode straight into the GENERIC (intermediary) messages, so the protocol-agnostic
// handlers (MovePlayer/HandleDigging) serve legacy and modern alike — no legacy-specific handler path.

/// <summary>0x0B Player Position → generic <see cref="SetPlayerPositionC2S"/> (drops the 1.5.2 Stance).</summary>
internal sealed class LegacyPositionToGenericCodec : ICodec<SetPlayerPositionC2S> {
    public void Encode(MinecraftStream s, SetPlayerPositionC2S m) => throw LegacySb.Outbound(nameof(SetPlayerPositionC2S));
    public SetPlayerPositionC2S Decode(MinecraftStream s) {
        double x = s.ReadDouble(), y = s.ReadDouble();
        s.ReadDouble(); // stance (eye Y) — not modeled
        return new(x, y, s.ReadDouble(), s.ReadBool());
    }
}

/// <summary>0x0C Player Look → generic <see cref="SetPlayerRotationC2S"/>.</summary>
internal sealed class LegacyLookToGenericCodec : ICodec<SetPlayerRotationC2S> {
    public void Encode(MinecraftStream s, SetPlayerRotationC2S m) => throw LegacySb.Outbound(nameof(SetPlayerRotationC2S));
    public SetPlayerRotationC2S Decode(MinecraftStream s) => new(s.ReadFloat(), s.ReadFloat(), s.ReadBool());
}

/// <summary>0x0D Player Position and Look → generic <see cref="SetPlayerPositionAndRotationC2S"/>.</summary>
internal sealed class LegacyPositionLookToGenericCodec : ICodec<SetPlayerPositionAndRotationC2S> {
    public void Encode(MinecraftStream s, SetPlayerPositionAndRotationC2S m) => throw LegacySb.Outbound(nameof(SetPlayerPositionAndRotationC2S));
    public SetPlayerPositionAndRotationC2S Decode(MinecraftStream s) {
        double x = s.ReadDouble(), y = s.ReadDouble();
        s.ReadDouble(); // stance
        double z = s.ReadDouble();
        return new(x, y, z, s.ReadFloat(), s.ReadFloat(), s.ReadBool());
    }
}

/// <summary>0x0E Player Digging → generic <see cref="PlayerActionC2S"/> (no Sequence in 1.5.2 → 0).</summary>
internal sealed class LegacyDiggingToGenericCodec : ICodec<PlayerActionC2S> {
    public void Encode(MinecraftStream s, PlayerActionC2S m) => throw LegacySb.Outbound(nameof(PlayerActionC2S));
    public PlayerActionC2S Decode(MinecraftStream s) {
        byte status = s.ReadUByte();
        int x = s.ReadInt(); byte y = s.ReadUByte(); int z = s.ReadInt();
        return new(status, new Vector3i(x, y, z), s.ReadUByte(), 0);
    }
}

internal sealed class LegacyBlockPlacementC2SCodec : ICodec<LegacyBlockPlacementC2S> {
    public void Encode(MinecraftStream s, LegacyBlockPlacementC2S m) => throw LegacySb.Outbound(nameof(LegacyBlockPlacementC2S));
    public LegacyBlockPlacementC2S Decode(MinecraftStream s) {
        int x = s.ReadInt(); byte y = s.ReadUByte(); int z = s.ReadInt(); byte dir = s.ReadUByte();
        var (id, _, damage) = s.ReadLegacySlot(); // the held item (creative provides it)
        return new(x, y, z, dir, id, damage, s.ReadUByte(), s.ReadUByte(), s.ReadUByte());
    }
}

internal sealed class LegacyHeldItemChangeC2SCodec : ICodec<LegacyHeldItemChangeC2S> {
    public void Encode(MinecraftStream s, LegacyHeldItemChangeC2S m) => throw LegacySb.Outbound(nameof(LegacyHeldItemChangeC2S));
    public LegacyHeldItemChangeC2S Decode(MinecraftStream s) => new(s.ReadShort());
}

/// <summary>0x12 Animation (the client's own arm swing) → generic <see cref="SwingArmC2S"/>; the EID
/// it carries is the client's own and is ignored (the server resolves the player from the connection).</summary>
internal sealed class LegacySwingToGenericCodec : ICodec<SwingArmC2S> {
    public void Encode(MinecraftStream s, SwingArmC2S m) => throw LegacySb.Outbound(nameof(SwingArmC2S));
    public SwingArmC2S Decode(MinecraftStream s) {
        s.ReadInt();   // entity id (the client's own)
        s.ReadUByte(); // animation (1 = swing arm)
        return new SwingArmC2S(0); // main hand
    }
}

/// <summary>0x13 Entity Action (the client toggling sneak/sprint) → generic <see cref="EntityActionC2S"/>;
/// the EID it carries is the client's own and is ignored.</summary>
internal sealed class LegacyEntityActionToGenericCodec : ICodec<EntityActionC2S> {
    public void Encode(MinecraftStream s, EntityActionC2S m) => throw LegacySb.Outbound(nameof(EntityActionC2S));
    public EntityActionC2S Decode(MinecraftStream s) {
        s.ReadInt();             // entity id (the client's own)
        int action = s.ReadUByte();
        return new EntityActionC2S(action switch {
            1 => EntityActionKind.StartSneaking,
            2 => EntityActionKind.StopSneaking,
            4 => EntityActionKind.StartSprinting,
            5 => EntityActionKind.StopSprinting,
            _ => EntityActionKind.Other,
        });
    }
}

internal sealed class LegacyCloseWindowC2SCodec : ICodec<LegacyCloseWindowC2S> {
    public void Encode(MinecraftStream s, LegacyCloseWindowC2S m) => throw LegacySb.Outbound(nameof(LegacyCloseWindowC2S));
    public LegacyCloseWindowC2S Decode(MinecraftStream s) => new(s.ReadUByte());
}

internal sealed class LegacyClickWindowC2SCodec : ICodec<LegacyClickWindowC2S> {
    public void Encode(MinecraftStream s, LegacyClickWindowC2S m) => throw LegacySb.Outbound(nameof(LegacyClickWindowC2S));
    public LegacyClickWindowC2S Decode(MinecraftStream s) {
        byte win = s.ReadUByte(); short slot = s.ReadShort(); byte btn = s.ReadUByte();
        short action = s.ReadShort(); byte mode = s.ReadUByte();
        s.SkipLegacySlot();
        return new(win, slot, btn, action, mode);
    }
}

internal sealed class LegacyConfirmTransactionC2SCodec : ICodec<LegacyConfirmTransactionC2S> {
    public void Encode(MinecraftStream s, LegacyConfirmTransactionC2S m) => throw LegacySb.Outbound(nameof(LegacyConfirmTransactionC2S));
    public LegacyConfirmTransactionC2S Decode(MinecraftStream s) => new(s.ReadUByte(), s.ReadShort(), s.ReadBool());
}

internal sealed class LegacyCreativeActionC2SCodec : ICodec<LegacyCreativeActionC2S> {
    public void Encode(MinecraftStream s, LegacyCreativeActionC2S m) => throw LegacySb.Outbound(nameof(LegacyCreativeActionC2S));
    public LegacyCreativeActionC2S Decode(MinecraftStream s) {
        short slot = s.ReadShort();
        s.SkipLegacySlot();
        return new(slot);
    }
}

internal sealed class LegacyEnchantItemC2SCodec : ICodec<LegacyEnchantItemC2S> {
    public void Encode(MinecraftStream s, LegacyEnchantItemC2S m) => throw LegacySb.Outbound(nameof(LegacyEnchantItemC2S));
    public LegacyEnchantItemC2S Decode(MinecraftStream s) => new(s.ReadUByte(), s.ReadUByte());
}

internal sealed class LegacyUpdateSignC2SCodec : ICodec<LegacyUpdateSignC2S> {
    public void Encode(MinecraftStream s, LegacyUpdateSignC2S m) => throw LegacySb.Outbound(nameof(LegacyUpdateSignC2S));
    public LegacyUpdateSignC2S Decode(MinecraftStream s) =>
        new(s.ReadInt(), s.ReadShort(), s.ReadInt(), s.ReadString16(), s.ReadString16(), s.ReadString16(), s.ReadString16());
}

internal sealed class LegacyPlayerAbilitiesC2SCodec : ICodec<LegacyPlayerAbilitiesC2S> {
    public void Encode(MinecraftStream s, LegacyPlayerAbilitiesC2S m) => throw LegacySb.Outbound(nameof(LegacyPlayerAbilitiesC2S));
    public LegacyPlayerAbilitiesC2S Decode(MinecraftStream s) => new(s.ReadUByte(), s.ReadUByte(), s.ReadUByte());
}

internal sealed class LegacyTabCompleteC2SCodec : ICodec<LegacyTabCompleteC2S> {
    public void Encode(MinecraftStream s, LegacyTabCompleteC2S m) => throw LegacySb.Outbound(nameof(LegacyTabCompleteC2S));
    public LegacyTabCompleteC2S Decode(MinecraftStream s) => new(s.ReadString16());
}

internal sealed class LegacyDisconnectC2SCodec : ICodec<LegacyDisconnectC2S> {
    public void Encode(MinecraftStream s, LegacyDisconnectC2S m) => throw LegacySb.Outbound(nameof(LegacyDisconnectC2S));
    public LegacyDisconnectC2S Decode(MinecraftStream s) => new(s.ReadString16());
}
