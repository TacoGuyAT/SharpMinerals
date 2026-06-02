using System.Collections.Concurrent;

namespace SharpMinerals.Network;

/// <summary>
/// A broadcast envelope holding one message and caching its framed wire bytes per protocol version,
/// so sending to many clients encodes each version only once.
/// </summary>
public sealed class CachedPacket {
    readonly ConcurrentDictionary<int, byte[]> byVersion = new();

    public IMessage Message { get; }

    public CachedPacket(IMessage message) => Message = message;

    public byte[] Framed(Protocol protocol) =>
        byVersion.GetOrAdd(protocol.Version, _ => protocol.Frame(Message));
}
