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

    /// <summary>The protocol for a handshake version, or null if unsupported.</summary>
    public Protocol? For(int version) => byVersion.GetValueOrDefault(version);

    /// <summary>The protocol for a handshake version, falling back to <see cref="Default"/>.</summary>
    public Protocol ForOrDefault(int version) => byVersion.GetValueOrDefault(version) ?? Default;

    /// <summary>Every registered protocol.</summary>
    public IEnumerable<Protocol> All => byVersion.Values;
}
