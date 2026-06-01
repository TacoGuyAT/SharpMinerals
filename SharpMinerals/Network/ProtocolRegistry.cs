using System.Diagnostics.CodeAnalysis;
using SharpMinerals.Network.Protocols;

namespace SharpMinerals.Network;

/// <summary>
/// The set of protocol versions the server can speak, keyed by the handshake protocol
/// number. A connection's protocol is chosen from this registry by the version it
/// announces in the handshake, so clients on different (modern) versions can be served
/// from the same listener. <see cref="Default"/> is used until the handshake selects a
/// version and as the fallback for unknown versions.
/// </summary>
public sealed class ProtocolRegistry {
    readonly Dictionary<int, Protocol> byVersion = new();

    /// <summary>The fallback/primary protocol (used pre-handshake and for unknown versions).</summary>
    public Protocol Default { get; }

    public ProtocolRegistry(Protocol defaultProtocol, params Protocol[] others) {
        Default = defaultProtocol;
        Add(defaultProtocol);
        foreach (var p in others) Add(p);
    }

    void Add(Protocol protocol) => byVersion[protocol.Version] = protocol;

    /// <summary>The protocol for a handshake version; false if that version is unsupported.</summary>
    public bool TryGet(int version, [MaybeNullWhen(false)] out Protocol protocol) =>
        byVersion.TryGetValue(version, out protocol);

    /// <summary>The protocol for a handshake version, falling back to <see cref="Default"/>.</summary>
    public Protocol ForOrDefault(int version) => byVersion.GetValueOrDefault(version) ?? Default;

    /// <summary>Every registered protocol.</summary>
    public IEnumerable<Protocol> All => byVersion.Values;

    /// <summary>
    /// Picks a protocol from a connection's <b>first byte</b>, before any handshake is decoded.
    /// Legacy (pre-Netty) clients open with a raw packet id — <c>0xFE</c> (server-list ping) or
    /// <c>0x02</c> (handshake) — neither of which is a valid modern frame-length VarInt (a modern
    /// handshake length is a small value, never 0x02, and 0xFE has the VarInt continuation bit set).
    /// So those route to a registered <see cref="LegacyJavaProtocol"/>; everything else is modern
    /// and uses <see cref="Default"/> until its handshake selects an exact version.
    /// </summary>
    public Protocol Detect(int firstByte) =>
        firstByte is 0xFE or 0x02 && All.OfType<LegacyJavaProtocol>().FirstOrDefault() is { } legacy
            ? legacy
            : Default;
}
