using SharpMinerals.Network.Buffers;
using SharpMinerals.Network.Messages;

namespace SharpMinerals.Network.Protocols.JE61.Codecs;

// Clientbound world-streaming codecs (1.5.2). All clientbound-only.

/// <summary>Maps the GENERIC <see cref="KeepAliveS2C"/> to the legacy 0x00 int form.</summary>
internal sealed class LegacyKeepAliveS2CMapper : ICodec<KeepAliveS2C> {
    public void Encode(MinecraftStream s, KeepAliveS2C m) => s.WriteInt((int)m.Id);
    public KeepAliveS2C Decode(MinecraftStream s) => new(s.ReadInt());
}

/// <summary>Maps the GENERIC <see cref="BlockUpdateS2C"/> to the legacy 0x35 Block Change.</summary>
internal sealed class LegacyBlockChangeS2CMapper : ICodec<BlockUpdateS2C> {
    public void Encode(MinecraftStream s, BlockUpdateS2C m) {
        s.WriteInt((int)m.Position.X);
        s.WriteByte2((sbyte)m.Position.Y);
        s.WriteInt((int)m.Position.Z);
        int id = m.State is { } st ? s.Types!.StateId(st) : s.Types!.StateId(m.Block); // mapper set by EncodePayload
        s.WriteShort((short)id);
        s.WriteUByte(0); // block metadata (wool colour / facing not modeled here yet)
    }

    public BlockUpdateS2C Decode(MinecraftStream s) => throw new NotSupportedException("BlockUpdateS2C is clientbound only.");
}

internal sealed class LegacySpawnPositionS2CCodec : ICodec<LegacySpawnPositionS2C> {
    public void Encode(MinecraftStream s, LegacySpawnPositionS2C m) {
        s.WriteInt(m.X);
        s.WriteInt(m.Y);
        s.WriteInt(m.Z);
    }

    public LegacySpawnPositionS2C Decode(MinecraftStream s) =>
        throw new NotSupportedException("LegacySpawnPositionS2C is clientbound only.");
}

internal sealed class LegacyPlayerPositionLookS2CCodec : ICodec<LegacyPlayerPositionLookS2C> {
    const double EyeHeight = 1.62;

    // Clientbound field order is X, Stance, Y, Z (Y and Stance swapped vs serverbound).
    public void Encode(MinecraftStream s, LegacyPlayerPositionLookS2C m) {
        s.WriteDouble(m.X);
        s.WriteDouble(m.Y + EyeHeight); // stance
        s.WriteDouble(m.Y);
        s.WriteDouble(m.Z);
        s.WriteFloat(m.Yaw);
        s.WriteFloat(m.Pitch);
        s.WriteBool(m.OnGround);
    }

    public LegacyPlayerPositionLookS2C Decode(MinecraftStream s) =>
        throw new NotSupportedException("LegacyPlayerPositionLookS2C is clientbound only.");
}

internal sealed class LegacyChunkDataS2CCodec : ICodec<LegacyChunkDataS2C> {
    public void Encode(MinecraftStream s, LegacyChunkDataS2C m) {
        s.WriteInt(m.X);
        s.WriteInt(m.Z);
        s.WriteBool(m.GroundUpContinuous);
        s.WriteUShort((ushort)m.PrimaryBitmap);
        s.WriteUShort((ushort)m.AddBitmap);
        s.WriteInt(m.CompressedData.Length);
        s.Write(m.CompressedData, 0, m.CompressedData.Length);
    }

    public LegacyChunkDataS2C Decode(MinecraftStream s) =>
        throw new NotSupportedException("LegacyChunkDataS2C is clientbound only.");
}
