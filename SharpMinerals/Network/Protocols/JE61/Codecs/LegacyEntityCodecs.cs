using SharpMinerals.Entities;
using SharpMinerals.Network.Buffers;
using SharpMinerals.Network.Messages;

namespace SharpMinerals.Network.Protocols.JE61.Codecs;

// Clientbound entity visibility - maps the GENERIC visibility messages to 1.5.2 wire forms so a legacy
// client SEES other players. Positions are absolute-integer fixed-point (block coord x 32); rotations
// are packed angle bytes (WriteAngle). All clientbound-only.

/// <summary>GENERIC <see cref="SpawnPlayerS2C"/> -> legacy 0x14 Spawn Named Entity.</summary>
internal sealed class LegacySpawnNamedEntityS2CMapper : ICodec<SpawnPlayerS2C> {
    public void Encode(MinecraftStream s, SpawnPlayerS2C m) {
        s.WriteInt(m.EntityId);
        s.WriteString16(m.Name);
        s.WriteInt((int)(m.X * 32));
        s.WriteInt((int)(m.Y * 32));
        s.WriteInt((int)(m.Z * 32));
        s.WriteAngle(m.Yaw);
        s.WriteAngle(m.Pitch);
        s.WriteShort(0); // current item (0 = none; a negative value crashes the client)
        // The 1.3+ client CRASHES on empty metadata, so send one key (index 0 entity-flags byte = 0) + end.
        s.WriteUByte(0x00); // header: index 0, type 0 (byte)
        s.WriteUByte(0x00); // value
        s.WriteUByte(0x7F); // metadata terminator
    }

    public SpawnPlayerS2C Decode(MinecraftStream s) => throw new NotSupportedException("SpawnPlayerS2C is clientbound only.");
}

/// <summary>GENERIC <see cref="TeleportEntityS2C"/> -> legacy 0x22 Entity Teleport.</summary>
internal sealed class LegacyEntityTeleportS2CMapper : ICodec<TeleportEntityS2C> {
    public void Encode(MinecraftStream s, TeleportEntityS2C m) {
        s.WriteInt(m.EntityId);
        s.WriteInt((int)(m.X * 32));
        s.WriteInt((int)(m.Y * 32));
        s.WriteInt((int)(m.Z * 32));
        s.WriteAngle(m.Yaw);
        s.WriteAngle(m.Pitch);
    }

    public TeleportEntityS2C Decode(MinecraftStream s) => throw new NotSupportedException("TeleportEntityS2C is clientbound only.");
}

/// <summary>GENERIC <see cref="RemoveEntitiesS2C"/> -> legacy 0x1D Destroy Entity (byte count + int ids).</summary>
internal sealed class LegacyDestroyEntityS2CMapper : ICodec<RemoveEntitiesS2C> {
    public void Encode(MinecraftStream s, RemoveEntitiesS2C m) {
        s.WriteByte2((sbyte)m.EntityIds.Count);
        foreach (var id in m.EntityIds) s.WriteInt(id);
    }

    public RemoveEntitiesS2C Decode(MinecraftStream s) => throw new NotSupportedException("RemoveEntitiesS2C is clientbound only.");
}

/// <summary>GENERIC <see cref="EntityHeadRotationS2C"/> -> legacy 0x23 Entity Head Look.</summary>
internal sealed class LegacyEntityHeadLookS2CMapper : ICodec<EntityHeadRotationS2C> {
    public void Encode(MinecraftStream s, EntityHeadRotationS2C m) {
        s.WriteInt(m.EntityId);
        s.WriteAngle(m.HeadYaw);
    }

    public EntityHeadRotationS2C Decode(MinecraftStream s) => throw new NotSupportedException("EntityHeadRotationS2C is clientbound only.");
}

/// <summary>GENERIC <see cref="EntityAnimationS2C"/> -> legacy 0x12 Animation (1.5.2 has only "swing arm" = 1).</summary>
internal sealed class LegacyEntityAnimationS2CMapper : ICodec<EntityAnimationS2C> {
    public void Encode(MinecraftStream s, EntityAnimationS2C m) {
        s.WriteInt(m.EntityId);
        s.WriteUByte(1); // swing arm
    }

    public EntityAnimationS2C Decode(MinecraftStream s) => throw new NotSupportedException("EntityAnimationS2C is clientbound only.");
}

/// <summary>GENERIC <see cref="SetEquipmentS2C"/> -> legacy 0x05 Entity Equipment (one slot per packet).
/// 1.5.2 slot ids differ from modern: 0=held, 1=boots, 2=leggings, 3=chestplate, 4=helmet. 1.5.2 has no
/// off-hand (added in 1.9), and the server never broadcasts one, so off-hand is unsupported here. Item
/// metadata (e.g. wool colour) isn't mapped for legacy yet, so damage is 0.</summary>
internal sealed class LegacyEntityEquipmentS2CMapper : ICodec<SetEquipmentS2C> {
    public void Encode(MinecraftStream s, SetEquipmentS2C m) {
        s.WriteInt(m.EntityId);
        s.WriteShort(LegacySlot(m.Slot));
        if (m.Item.IsEmpty)
            s.WriteLegacySlot(-1, 0, 0);
        else
            s.WriteLegacySlot((short)s.Types!.ItemId(m.Item), (byte)m.Item.Count, 0); // mapper set by EncodePayload
    }

    // Modern EquipmentSlot -> 1.5.2 equipment slot id. Off-hand has no 1.5.2 equivalent and is never sent.
    static short LegacySlot(EquipmentSlot slot) => slot switch {
        EquipmentSlot.MainHand => 0,
        EquipmentSlot.Boots => 1,
        EquipmentSlot.Leggings => 2,
        EquipmentSlot.Chestplate => 3,
        EquipmentSlot.Helmet => 4,
        _ => throw new NotSupportedException("1.5.2 has no off-hand equipment slot."),
    };

    public SetEquipmentS2C Decode(MinecraftStream s) => throw new NotSupportedException("SetEquipmentS2C is clientbound only.");
}

/// <summary>GENERIC <see cref="EntityFlagsS2C"/> -> legacy 0x28 Entity Metadata. 1.5.2 has no Pose, so the
/// state is just the shared-flags byte (index 0, type 0=byte); only the bits 1.5.2 shares - sneak 0x02 and
/// sprint 0x08 - are kept (modern-only bits like swimming 0x10 mean other things in 1.5.2, so mask them).</summary>
internal sealed class LegacyEntityMetadataS2CMapper : ICodec<EntityFlagsS2C> {
    const byte LegacyKnownFlags = (byte)(EntityFlags.Sneaking | EntityFlags.Sprinting); // 0x0A

    public void Encode(MinecraftStream s, EntityFlagsS2C m) {
        s.WriteInt(m.EntityId);
        s.WriteUByte(0x00); // header: index 0, type 0 (byte) -> shared flags
        s.WriteByte2((sbyte)((byte)m.Flags & LegacyKnownFlags));
        s.WriteUByte(0x7F); // metadata terminator
    }

    public EntityFlagsS2C Decode(MinecraftStream s) => throw new NotSupportedException("EntityFlagsS2C is clientbound only.");
}
