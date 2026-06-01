using SharpMinerals.Network.Buffers;
using SharpMinerals.Network.Messages;

namespace SharpMinerals.Network.Protocols.JE61.Codecs;

// ── Serverbound ───────────────────────────────────────────────────────────────

internal sealed class LegacyServerListPingC2SCodec : ICodec<LegacyServerListPingC2S> {
    public void Encode(MinecraftStream s, LegacyServerListPingC2S m) => s.WriteUByte(m.Magic);
    public LegacyServerListPingC2S Decode(MinecraftStream s) => new(s.ReadUByte());
}

// ── Clientbound ───────────────────────────────────────────────────────────────

internal sealed class LegacyKickS2CCodec : ICodec<LegacyKickS2C> {
    public void Encode(MinecraftStream s, LegacyKickS2C m) => s.WriteString16(m.Text);
    public LegacyKickS2C Decode(MinecraftStream s) => new(s.ReadString16());
}
