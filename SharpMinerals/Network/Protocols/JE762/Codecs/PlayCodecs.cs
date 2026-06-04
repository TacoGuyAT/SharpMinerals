using SharpMinerals.Math;
using SharpMinerals.Network.Buffers;
using SharpMinerals.Network.Messages;
using SharpMinerals.Network.Nbt;

namespace SharpMinerals.Network.Protocols.JE762.Codecs;

// -- Clientbound --------------------------------------------------------------

internal sealed class BundleDelimiterS2CCodec : ICodec<BundleDelimiterS2C> {
    public void Encode(MinecraftStream s, BundleDelimiterS2C m) { } // no payload
    public BundleDelimiterS2C Decode(MinecraftStream s) => new();
}

internal sealed class JoinGameS2CCodec : ICodec<JoinGameS2C> {
    // The Login (play) 0x28 body is identical for 1.19.4 (762) and 1.20.1 (763) EXCEPT 1.20 appended a trailing
    // portal-cooldown VarInt. Default off = 1.19.4 shape; ProtocolJE763 registers it with portalCooldown: true.
    readonly bool portalCooldown;
    public JoinGameS2CCodec(bool portalCooldown = false) => this.portalCooldown = portalCooldown;

    // Field order for 1.20.1 Login (play), 0x28. The registry codec NBT sits
    // between the dimension-name array and the dimension type identifier.
    public void Encode(MinecraftStream s, JoinGameS2C m) {
        s.WriteInt(m.EntityId);
        s.WriteBool(false);                       // is hardcore
        s.WriteUByte(m.GameMode);                 // game mode
        s.WriteByte2(-1);                         // previous game mode (-1 = none)
        s.WriteVarInt(1);                         // dimension count
        s.WriteString(m.DimensionName);           // dimension names[0]
        RegistryCodec.Default.WriteRoot(s);       // registry codec (named-root NBT)
        s.WriteString("minecraft:overworld");     // dimension type (must exist in the registry)
        s.WriteString(m.DimensionName);           // dimension (world) name
        s.WriteLong(m.HashedSeed);
        s.WriteVarInt(0);                         // max players (unused by the client)
        s.WriteVarInt(m.ViewDistance);
        s.WriteVarInt(m.ViewDistance);            // simulation distance
        s.WriteBool(m.ReducedDebugInfo);
        s.WriteBool(true);                        // enable respawn screen
        s.WriteBool(false);                       // is debug
        s.WriteBool(true);                        // is flat
        s.WriteBool(false);                       // has death location
        if (portalCooldown) s.WriteVarInt(0);     // portal cooldown (added in 1.20; absent in 1.19.4)
    }

    // Clientbound only - decoding would require an NBT reader, which the server
    // never needs (it only ever sends this packet).
    public JoinGameS2C Decode(MinecraftStream s) =>
        throw new NotSupportedException("JoinGameS2C is clientbound only.");
}

internal sealed class KeepAliveS2CCodec : ICodec<KeepAliveS2C> {
    public void Encode(MinecraftStream s, KeepAliveS2C m) => s.WriteLong(m.Id);
    public KeepAliveS2C Decode(MinecraftStream s) => new(s.ReadLong());
}

/// <summary>Respawn (0x41). Same dimension fields as Join Game's tail, minus the registry codec/entity id.
/// copyMetadata=false resets the player's client-side metadata for a clean reload.</summary>
internal sealed class RespawnS2CCodec : ICodec<RespawnS2C> {
    readonly bool portalCooldown; // 1.20 appended it; absent in 1.19.4 (see JoinGameS2CCodec)
    public RespawnS2CCodec(bool portalCooldown = false) => this.portalCooldown = portalCooldown;

    public void Encode(MinecraftStream s, RespawnS2C m) {
        s.WriteString(m.DimensionType);   // dimension type (must exist in the registry)
        s.WriteString(m.WorldName);       // dimension (world) name
        s.WriteLong(m.HashedSeed);
        s.WriteUByte(m.GameMode);         // game mode
        s.WriteUByte(0xFF);               // previous game mode (0xFF = none)
        s.WriteBool(false);               // is debug
        s.WriteBool(m.IsFlat);            // is flat
        s.WriteBool(false);               // copy metadata (reset)
        s.WriteBool(false);               // has death location
        if (portalCooldown) s.WriteVarInt(0); // portal cooldown (added in 1.20; absent in 1.19.4)
    }

    public RespawnS2C Decode(MinecraftStream s) =>
        throw new NotSupportedException("RespawnS2C is clientbound only.");
}

internal sealed class SetHealthS2CCodec : ICodec<SetHealthS2C> {
    public void Encode(MinecraftStream s, SetHealthS2C m) {
        s.WriteFloat(m.Health);
        s.WriteVarInt(m.Food);
        s.WriteFloat(m.Saturation);
    }

    public SetHealthS2C Decode(MinecraftStream s) => new(s.ReadFloat(), s.ReadVarInt(), s.ReadFloat());
}

internal sealed class SetCenterChunkS2CCodec : ICodec<SetCenterChunkS2C> {
    public void Encode(MinecraftStream s, SetCenterChunkS2C m) {
        s.WriteVarInt(m.ChunkX);
        s.WriteVarInt(m.ChunkZ);
    }

    public SetCenterChunkS2C Decode(MinecraftStream s) => new(s.ReadVarInt(), s.ReadVarInt());
}

internal sealed class SetDefaultSpawnPositionS2CCodec : ICodec<SetDefaultSpawnPositionS2C> {
    public void Encode(MinecraftStream s, SetDefaultSpawnPositionS2C m) {
        s.WritePosition(m.Position.X, m.Position.Y, m.Position.Z);
        s.WriteFloat(m.Angle);
    }

    public SetDefaultSpawnPositionS2C Decode(MinecraftStream s) {
        var (x, y, z) = s.ReadPosition();
        return new SetDefaultSpawnPositionS2C(new Vector3i(x, y, z), s.ReadFloat());
    }
}

internal sealed class SynchronizePlayerPositionS2CCodec : ICodec<SynchronizePlayerPositionS2C> {
    public void Encode(MinecraftStream s, SynchronizePlayerPositionS2C m) {
        s.WriteDouble(m.X);
        s.WriteDouble(m.Y);
        s.WriteDouble(m.Z);
        s.WriteFloat(m.Yaw);
        s.WriteFloat(m.Pitch);
        s.WriteUByte(0);               // relative flags (all absolute)
        s.WriteVarInt(m.TeleportId);
        // NOTE: 1.20.1 has NO trailing "dismount vehicle" boolean here.
    }

    public SynchronizePlayerPositionS2C Decode(MinecraftStream s) {
        double x = s.ReadDouble(), y = s.ReadDouble(), z = s.ReadDouble();
        float yaw = s.ReadFloat(), pitch = s.ReadFloat();
        s.ReadUByte();                 // flags
        return new SynchronizePlayerPositionS2C(x, y, z, yaw, pitch, s.ReadVarInt());
    }
}

internal sealed class BlockUpdateS2CCodec : ICodec<BlockUpdateS2C> {
    public void Encode(MinecraftStream s, BlockUpdateS2C m) {
        s.WritePosition(m.Position.X, m.Position.Y, m.Position.Z);
        // mapper set by Protocol.EncodePayload; no state override -> the block's default state id.
        s.WriteVarInt(m.State is { } st ? s.Types!.StateId(st) : s.Types!.StateId(m.Block));
    }

    // Clientbound only - there is no reverse map from a wire state id back to our BlockState.
    public BlockUpdateS2C Decode(MinecraftStream s) =>
        throw new NotSupportedException("BlockUpdateS2C is clientbound only.");
}

internal sealed class ChunkDataS2CCodec : ICodec<ChunkDataS2C> {
    // The whole packet body is built by ChunkSerializer; write it as-is.
    public void Encode(MinecraftStream s, ChunkDataS2C m) => s.Write(m.Payload, 0, m.Payload.Length);
    public ChunkDataS2C Decode(MinecraftStream s) =>
        throw new NotSupportedException("ChunkDataS2C is clientbound only.");
}

internal sealed class AckBlockChangeS2CCodec : ICodec<AckBlockChangeS2C> {
    public void Encode(MinecraftStream s, AckBlockChangeS2C m) => s.WriteVarInt(m.Sequence);
    public AckBlockChangeS2C Decode(MinecraftStream s) => new(s.ReadVarInt());
}

internal sealed class SetItemEntityMetadataS2CCodec : ICodec<SetItemEntityMetadataS2C> {
    const byte ItemDataIndex = 8;   // ItemEntity's item slot lives at metadata index 8
    const int SlotMetadataType = 7; // metadata serializer type id for Slot
    const byte MetadataEnd = 0xFF;

    public void Encode(MinecraftStream s, SetItemEntityMetadataS2C m) {
        s.WriteVarInt(m.EntityId);
        s.WriteUByte(ItemDataIndex);
        s.WriteVarInt(SlotMetadataType);
        SlotWire.WriteStack(s, m.Stack); // maps our ItemStack -> wire Slot via s.Types
        s.WriteUByte(MetadataEnd);
    }

    public SetItemEntityMetadataS2C Decode(MinecraftStream s) =>
        throw new NotSupportedException("SetItemEntityMetadataS2C is clientbound only.");
}

internal sealed class SpawnEntityS2CCodec : ICodec<SpawnEntityS2C> {
    public void Encode(MinecraftStream s, SpawnEntityS2C m) {
        s.WriteVarInt(m.EntityId);
        s.WriteUuid(m.Uuid);
        s.WriteVarInt(s.Types!.EntityTypeId(m.Type)); // mapper set by Protocol.EncodePayload
        s.WriteDouble(m.X);
        s.WriteDouble(m.Y);
        s.WriteDouble(m.Z);
        s.WriteUByte(m.Pitch);
        s.WriteUByte(m.Yaw);
        s.WriteUByte(m.HeadYaw);
        // A falling_block carries its block-state id in Data; resolve it per protocol via the mapper.
        s.WriteVarInt(m.BlockData is { } block ? s.Types!.StateId(block) : m.Data);
        s.WriteShort(m.VelocityX);
        s.WriteShort(m.VelocityY);
        s.WriteShort(m.VelocityZ);
    }

    // Clientbound only - there is no reverse map from a wire entity-type id back to our EntityType.
    public SpawnEntityS2C Decode(MinecraftStream s) =>
        throw new NotSupportedException("SpawnEntityS2C is clientbound only.");
}

internal sealed class SetEntityVelocityS2CCodec : ICodec<SetEntityVelocityS2C> {
    public void Encode(MinecraftStream s, SetEntityVelocityS2C m) {
        s.WriteVarInt(m.EntityId);
        s.WriteShort(m.VelocityX);
        s.WriteShort(m.VelocityY);
        s.WriteShort(m.VelocityZ);
    }

    public SetEntityVelocityS2C Decode(MinecraftStream s) =>
        throw new NotSupportedException("SetEntityVelocityS2C is clientbound only.");
}

internal sealed class CollectItemS2CCodec : ICodec<CollectItemS2C> {
    public void Encode(MinecraftStream s, CollectItemS2C m) {
        s.WriteVarInt(m.CollectedEntityId);
        s.WriteVarInt(m.CollectorEntityId);
        s.WriteVarInt(m.PickupItemCount);
    }

    public CollectItemS2C Decode(MinecraftStream s) =>
        throw new NotSupportedException("CollectItemS2C is clientbound only.");
}

// -- Serverbound --------------------------------------------------------------

internal sealed class KeepAliveC2SCodec : ICodec<KeepAliveC2S> {
    public void Encode(MinecraftStream s, KeepAliveC2S m) => s.WriteLong(m.Id);
    public KeepAliveC2S Decode(MinecraftStream s) => new(s.ReadLong());
}

internal sealed class ConfirmTeleportationC2SCodec : ICodec<ConfirmTeleportationC2S> {
    public void Encode(MinecraftStream s, ConfirmTeleportationC2S m) => s.WriteVarInt(m.TeleportId);
    public ConfirmTeleportationC2S Decode(MinecraftStream s) => new(s.ReadVarInt());
}

internal sealed class SetPlayerPositionC2SCodec : ICodec<SetPlayerPositionC2S> {
    public void Encode(MinecraftStream s, SetPlayerPositionC2S m) {
        s.WriteDouble(m.X);
        s.WriteDouble(m.Y);
        s.WriteDouble(m.Z);
        s.WriteBool(m.OnGround);
    }

    public SetPlayerPositionC2S Decode(MinecraftStream s) =>
        new(s.ReadDouble(), s.ReadDouble(), s.ReadDouble(), s.ReadBool());
}

internal sealed class PlayerActionC2SCodec : ICodec<PlayerActionC2S> {
    public void Encode(MinecraftStream s, PlayerActionC2S m) {
        s.WriteVarInt(m.Status);
        s.WritePosition(m.Position.X, m.Position.Y, m.Position.Z);
        s.WriteUByte(m.Face);
        s.WriteVarInt(m.Sequence);
    }

    public PlayerActionC2S Decode(MinecraftStream s) {
        int status = s.ReadVarInt();
        var (x, y, z) = s.ReadPosition();
        byte face = s.ReadUByte();
        return new PlayerActionC2S(status, new Vector3i(x, y, z), face, s.ReadVarInt());
    }
}

internal sealed class UseItemOnC2SCodec : ICodec<UseItemOnC2S> {
    public void Encode(MinecraftStream s, UseItemOnC2S m) {
        s.WriteVarInt(m.Hand);
        s.WritePosition(m.Position.X, m.Position.Y, m.Position.Z);
        s.WriteVarInt(m.Face);
        s.WriteFloat(m.CursorX);
        s.WriteFloat(m.CursorY);
        s.WriteFloat(m.CursorZ);
        s.WriteBool(m.InsideBlock);
        s.WriteVarInt(m.Sequence);
    }

    public UseItemOnC2S Decode(MinecraftStream s) {
        int hand = s.ReadVarInt();
        var (x, y, z) = s.ReadPosition();
        int face = s.ReadVarInt();
        float cx = s.ReadFloat(), cy = s.ReadFloat(), cz = s.ReadFloat();
        bool inside = s.ReadBool();
        return new UseItemOnC2S(hand, new Vector3i(x, y, z), face, cx, cy, cz, inside, s.ReadVarInt());
    }
}
