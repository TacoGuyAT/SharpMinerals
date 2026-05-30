using SharpMinerals.Network.Buffers;
using SharpMinerals.Network.Messages;

namespace SharpMinerals.Network.Protocols.JE763.Codecs;

internal sealed class StatusRequestC2SCodec : ICodec<StatusRequestC2S> {
    public void Encode(MinecraftStream s, StatusRequestC2S m) { }
    public StatusRequestC2S Decode(MinecraftStream s) => new();
}

internal sealed class StatusResponseS2CCodec : ICodec<StatusResponseS2C> {
    public void Encode(MinecraftStream s, StatusResponseS2C m) => s.WriteString(m.Json);
    public StatusResponseS2C Decode(MinecraftStream s) => new(s.ReadString());
}

internal sealed class PingRequestC2SCodec : ICodec<PingRequestC2S> {
    public void Encode(MinecraftStream s, PingRequestC2S m) => s.WriteLong(m.Payload);
    public PingRequestC2S Decode(MinecraftStream s) => new(s.ReadLong());
}

internal sealed class PongResponseS2CCodec : ICodec<PongResponseS2C> {
    public void Encode(MinecraftStream s, PongResponseS2C m) => s.WriteLong(m.Payload);
    public PongResponseS2C Decode(MinecraftStream s) => new(s.ReadLong());
}
