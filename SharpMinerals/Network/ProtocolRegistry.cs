using System.Diagnostics.CodeAnalysis;
using SharpMinerals.Network.Protocols;

namespace SharpMinerals.Network;

/// <summary>
/// The set of protocol versions the server can speak, keyed by handshake protocol number, so clients
/// on different versions share one listener. <see cref="Default"/> is used pre-handshake and as fallback.
/// </summary>
public sealed class ProtocolRegistry {
    readonly Dictionary<int, Protocol> byVersion = new();

    public Protocol Default { get; }

    public ProtocolRegistry(Protocol defaultProtocol, params Protocol[] others) {
        Default = defaultProtocol;
        Add(defaultProtocol);
        foreach (var p in others) Add(p);
    }

    void Add(Protocol protocol) => byVersion[protocol.Version] = protocol;

    public bool TryGet(int version, [MaybeNullWhen(false)] out Protocol protocol) =>
        byVersion.TryGetValue(version, out protocol);

    public Protocol ForOrDefault(int version) => byVersion.GetValueOrDefault(version) ?? Default;

    public IEnumerable<Protocol> All => byVersion.Values;

    /// <summary>
    /// Picks a protocol from a connection's first byte, before any handshake is decoded.
    /// Legacy clients open with raw id 0xFE (ping) or 0x02 (handshake), neither a valid modern
    /// frame-length VarInt, so those route to <see cref="LegacyJavaProtocol"/>; else <see cref="Default"/>.
    /// </summary>
    public Protocol Detect(int firstByte) =>
        firstByte is 0xFE or 0x02 && All.OfType<LegacyJavaProtocol>().FirstOrDefault() is { } legacy
            ? legacy
            : Default;
}
