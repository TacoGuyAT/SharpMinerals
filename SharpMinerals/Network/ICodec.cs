using SharpMinerals.Network.Buffers;

namespace SharpMinerals.Network;

/// <summary>
/// Converts a single message type to and from the wire for a given protocol
/// version. Codecs operate on a <see cref="MinecraftStream"/> positioned at the
/// start of the packet's payload (the packet id has already been read/written by
/// the framing layer).
/// </summary>
public interface ICodec {
    void Encode(MinecraftStream stream, IMessage message);
    IMessage Decode(MinecraftStream stream);
}

/// <summary>Strongly-typed convenience layer over <see cref="ICodec"/>.</summary>
public interface ICodec<T> : ICodec where T : IMessage {
    void ICodec.Encode(MinecraftStream stream, IMessage message) => Encode(stream, (T)message);
    IMessage ICodec.Decode(MinecraftStream stream) => Decode(stream);

    void Encode(MinecraftStream stream, T message);
    new T Decode(MinecraftStream stream);
}
