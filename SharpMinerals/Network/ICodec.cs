using SharpMinerals.Network.Buffers;

namespace SharpMinerals.Network;

/// <summary>
/// Converts a single message type to/from the wire for a protocol version. Operates on a
/// <see cref="MinecraftStream"/> positioned at the payload start (packet id handled by the framing layer).
/// </summary>
public interface ICodec {
    void Encode(MinecraftStream stream, IMessage message);
    IMessage Decode(MinecraftStream stream);
}

public interface ICodec<T> : ICodec where T : IMessage {
    void ICodec.Encode(MinecraftStream stream, IMessage message) => Encode(stream, (T)message);
    IMessage ICodec.Decode(MinecraftStream stream) => Decode(stream);

    void Encode(MinecraftStream stream, T message);
    new T Decode(MinecraftStream stream);
}
