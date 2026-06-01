using System.Collections.Concurrent;

namespace SharpMinerals.Network;

/// <summary>
/// A broadcast envelope: holds one message and caches its fully-framed wire bytes <b>per protocol
/// version</b>, so sending to many clients encodes each version only once (today's <c>Broadcast</c>
/// re-encodes per client). The original message is kept ("packed in") so any version can be framed
/// on demand. Once messages carry protocol-agnostic domain types mapped per-version at encode time,
/// each version's bytes differ; today (a single version) this simply dedupes the per-client re-encode.
/// </summary>
public sealed class CachedPacket {
    readonly ConcurrentDictionary<int, byte[]> byVersion = new();

    /// <summary>The original message, kept so any version can be encoded on demand.</summary>
    public IMessage Message { get; }

    public CachedPacket(IMessage message) => Message = message;

    /// <summary>The framed wire bytes for a protocol version — encoded once, then cached.</summary>
    public byte[] Framed(Protocol protocol) =>
        byVersion.GetOrAdd(protocol.Version, _ => protocol.Frame(Message));
}
