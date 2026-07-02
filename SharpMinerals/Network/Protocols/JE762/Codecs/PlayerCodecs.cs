using SharpMinerals.Entities;
using SharpMinerals.Network.Buffers;
using SharpMinerals.Network.Messages;

namespace SharpMinerals.Network.Protocols.JE762.Codecs;

// -- Clientbound --------------------------------------------------------------

internal sealed class PlayerInfoUpdateS2CCodec : ICodec<PlayerInfoUpdateS2C> {
    // Action bitmask: ADD_PLAYER(0x01) | UPDATE_GAME_MODE(0x04) | UPDATE_LISTED(0x08) | UPDATE_LATENCY(0x10).
    const byte Actions = 0x01 | 0x04 | 0x08 | 0x10;

    public void Encode(MinecraftStream s, PlayerInfoUpdateS2C m) {
        s.WriteUByte(Actions);
        s.WriteVarInt(m.Entries.Count);
        // Per-entry data is written in action-ordinal order.
        foreach (var e in m.Entries) {
            s.WriteUuid(e.Uuid);
            s.WriteString(e.Name);   // ADD_PLAYER
            s.WriteVarInt(0);        //   property count
            s.WriteVarInt(e.GameMode); // UPDATE_GAME_MODE
            s.WriteBool(e.Listed);     // UPDATE_LISTED
            s.WriteVarInt(e.Latency);  // UPDATE_LATENCY
        }
    }

    public PlayerInfoUpdateS2C Decode(MinecraftStream s) =>
        throw new NotSupportedException("PlayerInfoUpdateS2C is clientbound only.");
}

internal sealed class PlayerInfoRemoveS2CCodec : ICodec<PlayerInfoRemoveS2C> {
    public void Encode(MinecraftStream s, PlayerInfoRemoveS2C m) {
        s.WriteVarInt(m.Uuids.Count);
        foreach (var uuid in m.Uuids) s.WriteUuid(uuid);
    }

    public PlayerInfoRemoveS2C Decode(MinecraftStream s) =>
        throw new NotSupportedException("PlayerInfoRemoveS2C is clientbound only.");
}

internal sealed class SpawnPlayerS2CCodec : ICodec<SpawnPlayerS2C> {
    public void Encode(MinecraftStream s, SpawnPlayerS2C m) {
        s.WriteVarInt(m.EntityId);
        s.WriteUuid(m.Uuid);
        s.WriteDouble(m.X);
        s.WriteDouble(m.Y);
        s.WriteDouble(m.Z);
        s.WriteAngle(m.Yaw);
        s.WriteAngle(m.Pitch);
    }

    public SpawnPlayerS2C Decode(MinecraftStream s) =>
        throw new NotSupportedException("SpawnPlayerS2C is clientbound only.");
}

internal sealed class TeleportEntityS2CCodec : ICodec<TeleportEntityS2C> {
    public void Encode(MinecraftStream s, TeleportEntityS2C m) {
        s.WriteVarInt(m.EntityId);
        s.WriteDouble(m.X);
        s.WriteDouble(m.Y);
        s.WriteDouble(m.Z);
        s.WriteAngle(m.Yaw);
        s.WriteAngle(m.Pitch);
        s.WriteBool(m.OnGround);
    }

    public TeleportEntityS2C Decode(MinecraftStream s) =>
        throw new NotSupportedException("TeleportEntityS2C is clientbound only.");
}

internal sealed class EntityHeadRotationS2CCodec : ICodec<EntityHeadRotationS2C> {
    public void Encode(MinecraftStream s, EntityHeadRotationS2C m) {
        s.WriteVarInt(m.EntityId);
        s.WriteAngle(m.HeadYaw);
    }

    public EntityHeadRotationS2C Decode(MinecraftStream s) =>
        throw new NotSupportedException("EntityHeadRotationS2C is clientbound only.");
}

internal sealed class RemoveEntitiesS2CCodec : ICodec<RemoveEntitiesS2C> {
    public void Encode(MinecraftStream s, RemoveEntitiesS2C m) {
        s.WriteVarInt(m.EntityIds.Count);
        foreach (var id in m.EntityIds) s.WriteVarInt(id);
    }

    public RemoveEntitiesS2C Decode(MinecraftStream s) =>
        throw new NotSupportedException("RemoveEntitiesS2C is clientbound only.");
}

internal sealed class EntityAnimationS2CCodec : ICodec<EntityAnimationS2C> {
    public void Encode(MinecraftStream s, EntityAnimationS2C m) {
        s.WriteVarInt(m.EntityId);
        s.WriteUByte(m.Animation switch {  // 1.20.1 animation ids
            EntityAnimation.SwingMainArm => 0,
            EntityAnimation.SwingOffArm => 3,
            _ => 0,
        });
    }

    public EntityAnimationS2C Decode(MinecraftStream s) =>
        throw new NotSupportedException("EntityAnimationS2C is clientbound only.");
}

/// <summary>Set Entity Metadata (0x52) carrying the entity's shared-flags state: the flags byte (index 0,
/// e.g. crouch 0x02 / sprint 0x08) and the derived Pose (index 6) so the model visibly crouches/swims.</summary>
internal sealed class EntityFlagsS2CCodec : ICodec<EntityFlagsS2C> {
    const byte FlagsIndex = 0; const int ByteType = 0;
    const byte PoseIndex = 6;  const int PoseType = 20; // 1.20.1 metadata serializer type for Pose
    const int StandingPose = 0, SwimmingPose = 3, SneakingPose = 5;
    const byte MetadataEnd = 0xFF;

    public void Encode(MinecraftStream s, EntityFlagsS2C m) {
        s.WriteVarInt(m.EntityId);
        s.WriteUByte(FlagsIndex); s.WriteVarInt(ByteType); s.WriteByte2((sbyte)(byte)m.Flags);
        int pose = m.Flags.HasFlag(EntityState.Swimming) ? SwimmingPose
                 : m.Flags.HasFlag(EntityState.Sneaking) ? SneakingPose
                 : StandingPose;
        s.WriteUByte(PoseIndex);  s.WriteVarInt(PoseType); s.WriteVarInt(pose);
        s.WriteUByte(MetadataEnd);
    }

    public EntityFlagsS2C Decode(MinecraftStream s) =>
        throw new NotSupportedException("EntityFlagsS2C is clientbound only.");
}

/// <summary>Set Equipment (0x55): entity id + a top-bit-terminated array of (slot i8, item Slot). We send
/// one slot per message, so the single entry has its top bit clear (no continuation).</summary>
internal sealed class SetEquipmentS2CCodec : ICodec<SetEquipmentS2C> {
    public void Encode(MinecraftStream s, SetEquipmentS2C m) {
        s.WriteVarInt(m.EntityId);
        s.WriteUByte((byte)m.Slot); // slot 0-5, top bit clear -> last (and only) entry
        SlotWire.WriteStack(s, m.Item);
    }

    public SetEquipmentS2C Decode(MinecraftStream s) =>
        throw new NotSupportedException("SetEquipmentS2C is clientbound only.");
}

// -- Serverbound --------------------------------------------------------------

internal sealed class SwingArmC2SCodec : ICodec<SwingArmC2S> {
    public void Encode(MinecraftStream s, SwingArmC2S m) =>
        throw new NotSupportedException("SwingArmC2S is serverbound only.");

    public SwingArmC2S Decode(MinecraftStream s) => new(s.ReadVarInt()); // hand
}

internal sealed class EntityActionC2SCodec : ICodec<EntityActionC2S> {
    public void Encode(MinecraftStream s, EntityActionC2S m) =>
        throw new NotSupportedException("EntityActionC2S is serverbound only.");

    public EntityActionC2S Decode(MinecraftStream s) {
        s.ReadVarInt();            // entity id (the client's own)
        int action = s.ReadVarInt();
        s.ReadVarInt();            // jump boost (horse only)
        return new EntityActionC2S(action switch {
            0 => EntityActionKind.StartSneaking,
            1 => EntityActionKind.StopSneaking,
            3 => EntityActionKind.StartSprinting,
            4 => EntityActionKind.StopSprinting,
            _ => EntityActionKind.Other,
        });
    }
}

internal sealed class SetPlayerPositionAndRotationC2SCodec : ICodec<SetPlayerPositionAndRotationC2S> {
    public void Encode(MinecraftStream s, SetPlayerPositionAndRotationC2S m) {
        s.WriteDouble(m.X); s.WriteDouble(m.Y); s.WriteDouble(m.Z);
        s.WriteFloat(m.Yaw); s.WriteFloat(m.Pitch);
        s.WriteBool(m.OnGround);
    }

    public SetPlayerPositionAndRotationC2S Decode(MinecraftStream s) =>
        new(s.ReadDouble(), s.ReadDouble(), s.ReadDouble(), s.ReadFloat(), s.ReadFloat(), s.ReadBool());
}

internal sealed class SetPlayerRotationC2SCodec : ICodec<SetPlayerRotationC2S> {
    public void Encode(MinecraftStream s, SetPlayerRotationC2S m) {
        s.WriteFloat(m.Yaw); s.WriteFloat(m.Pitch);
        s.WriteBool(m.OnGround);
    }

    public SetPlayerRotationC2S Decode(MinecraftStream s) =>
        new(s.ReadFloat(), s.ReadFloat(), s.ReadBool());
}

internal sealed class InteractEntityC2SCodec : ICodec<InteractEntityC2S> {
    public void Encode(MinecraftStream s, InteractEntityC2S m) {
        s.WriteVarInt(m.TargetId);
        s.WriteVarInt(m.Type);
        s.WriteBool(m.Sneaking);
    }

    public InteractEntityC2S Decode(MinecraftStream s) {
        int target = s.ReadVarInt();
        int type = s.ReadVarInt();
        if (type == 2) { s.ReadFloat(); s.ReadFloat(); s.ReadFloat(); } // interact_at target vector
        if (type == 0 || type == 2) s.ReadVarInt();                    // hand
        return new InteractEntityC2S(target, type, s.ReadBool());
    }
}
