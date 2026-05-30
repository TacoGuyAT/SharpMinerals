using System.Net.Sockets;

namespace SharpMinerals.Network;
public interface ICodec {
    public ReadOnlySpan<byte> Serialize(IMessage packet);
    public IMessage Deserialize(NetworkStream stream);
}

public interface ICodec<T> : ICodec where T : IMessage {
    ReadOnlySpan<byte> ICodec.Serialize(IMessage packet) => Serialize((T)packet);
    IMessage ICodec.Deserialize(NetworkStream stream) => Deserialize(stream);
    public ReadOnlySpan<byte> Serialize(T packet);
    public new T Deserialize(NetworkStream stream);
}
