using SharpMinerals.Network.Buffers;
using SharpMinerals.Network.Messages;

namespace SharpMinerals.Network.Protocols.JE763.Codecs;

// ── Clientbound ──────────────────────────────────────────────────────────────

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

// ── Serverbound ──────────────────────────────────────────────────────────────

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
