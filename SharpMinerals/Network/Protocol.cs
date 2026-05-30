using System.Collections.Concurrent;
using System.Net.Sockets;

namespace SharpMinerals.Network;
public abstract class Protocol {
    public ConcurrentDictionary<Type, ICodec> Codecs { get; private set; }
    public Protocol() {
        Codecs = new();
    }
    public void Register(ICodec codec, Type type) {
        Codecs.AddOrUpdate(type, (_) => codec, (_, _) => codec);
    }
    public ReadOnlySpan<byte> Serialize<T>(T packet) where T : IMessage {
        Codecs.TryGetValue(typeof(T), out var codec);
        return codec == null ? throw new Exception($"Codec for packet {typeof(T)} in {this.GetType()} is missing!") : codec.Serialize(packet);
    }
    public T Deserialize<T>(NetworkStream stream) where T : IMessage {
        Codecs.TryGetValue(typeof(T), out var codec);
        return codec == null ? throw new Exception($"Codec for packet {typeof(T)} in {this.GetType()} is missing!") : (T)codec.Deserialize(stream);
    }
}
