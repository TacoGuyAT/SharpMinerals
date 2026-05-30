using SharpMinerals.Network.Buffers;
using SharpMinerals.Network.Messages;

namespace SharpMinerals.Network.Protocols.JE763.Codecs;

internal sealed class HandshakeC2SCodec : ICodec<HandshakeC2S> {
    public void Encode(MinecraftStream s, HandshakeC2S m) {
        s.WriteVarInt(m.ProtocolVersion);
        s.WriteString(m.ServerAddress);
        s.WriteUShort(m.ServerPort);
        s.WriteVarInt(m.NextState);
    }

    public HandshakeC2S Decode(MinecraftStream s) =>
        new(s.ReadVarInt(), s.ReadString(255), s.ReadUShort(), s.ReadVarInt());
}
