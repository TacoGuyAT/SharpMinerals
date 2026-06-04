using SharpMinerals.Network.Buffers;
using SharpMinerals.Network.Messages;

namespace SharpMinerals.Network.Protocols.JE61.Codecs;

// The 1.5.2 login handshake (0x02 -> 0xFD -> 0xFC -> empty 0xFC -> 0xCD -> 0x01). Strings are UTF-16BE
// (string16); the encryption blobs are short-length-prefixed byte arrays.

// -- Serverbound ---------------------------------------------------------------

internal sealed class LegacyHandshakeC2SCodec : ICodec<LegacyHandshakeC2S> {
    public void Encode(MinecraftStream s, LegacyHandshakeC2S m) =>
        throw new NotSupportedException("LegacyHandshakeC2S is serverbound only.");

    public LegacyHandshakeC2S Decode(MinecraftStream s) =>
        new(s.ReadUByte(), s.ReadString16(), s.ReadString16(), s.ReadInt());
}

internal sealed class LegacyEncryptionResponseC2SCodec : ICodec<LegacyEncryptionResponseC2S> {
    public void Encode(MinecraftStream s, LegacyEncryptionResponseC2S m) =>
        throw new NotSupportedException("LegacyEncryptionResponseC2S is serverbound only.");

    public LegacyEncryptionResponseC2S Decode(MinecraftStream s) =>
        new(s.ReadByteArray16(), s.ReadByteArray16());
}

internal sealed class LegacyClientStatusesC2SCodec : ICodec<LegacyClientStatusesC2S> {
    public void Encode(MinecraftStream s, LegacyClientStatusesC2S m) =>
        throw new NotSupportedException("LegacyClientStatusesC2S is serverbound only.");

    public LegacyClientStatusesC2S Decode(MinecraftStream s) => new(s.ReadUByte());
}

internal sealed class LegacyClientSettingsC2SCodec : ICodec<LegacyClientSettingsC2S> {
    public void Encode(MinecraftStream s, LegacyClientSettingsC2S m) =>
        throw new NotSupportedException("LegacyClientSettingsC2S is serverbound only.");

    public LegacyClientSettingsC2S Decode(MinecraftStream s) =>
        new(s.ReadString16(), s.ReadUByte(), s.ReadUByte(), s.ReadUByte(), s.ReadBool());
}

internal sealed class LegacyPluginMessageC2SCodec : ICodec<LegacyPluginMessageC2S> {
    public void Encode(MinecraftStream s, LegacyPluginMessageC2S m) =>
        throw new NotSupportedException("LegacyPluginMessageC2S is serverbound only.");

    public LegacyPluginMessageC2S Decode(MinecraftStream s) => new(s.ReadString16(), s.ReadByteArray16());
}

// -- Clientbound ---------------------------------------------------------------

internal sealed class LegacyEncryptionRequestS2CCodec : ICodec<LegacyEncryptionRequestS2C> {
    public void Encode(MinecraftStream s, LegacyEncryptionRequestS2C m) {
        s.WriteString16(m.ServerId);
        s.WriteByteArray16(m.PublicKey);
        s.WriteByteArray16(m.VerifyToken);
    }

    public LegacyEncryptionRequestS2C Decode(MinecraftStream s) =>
        throw new NotSupportedException("LegacyEncryptionRequestS2C is clientbound only.");
}

internal sealed class LegacyEncryptionAcceptS2CCodec : ICodec<LegacyEncryptionAcceptS2C> {
    // Empty payload: two zero-length byte arrays (the signal for both sides to enable AES/CFB8).
    public void Encode(MinecraftStream s, LegacyEncryptionAcceptS2C m) {
        s.WriteByteArray16(Array.Empty<byte>());
        s.WriteByteArray16(Array.Empty<byte>());
    }

    public LegacyEncryptionAcceptS2C Decode(MinecraftStream s) =>
        throw new NotSupportedException("LegacyEncryptionAcceptS2C is clientbound only.");
}

internal sealed class LegacyLoginRequestS2CCodec : ICodec<LegacyLoginRequestS2C> {
    public void Encode(MinecraftStream s, LegacyLoginRequestS2C m) {
        s.WriteInt(m.EntityId);
        s.WriteString16(m.LevelType);
        s.WriteByte2((sbyte)m.GameMode);
        s.WriteByte2((sbyte)m.Dimension);
        s.WriteByte2((sbyte)m.Difficulty);
        s.WriteByte2(0);                    // "not used" (was world height)
        s.WriteByte2((sbyte)m.MaxPlayers);
    }

    public LegacyLoginRequestS2C Decode(MinecraftStream s) =>
        throw new NotSupportedException("LegacyLoginRequestS2C is clientbound only.");
}
