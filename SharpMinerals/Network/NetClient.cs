namespace SharpMinerals.Network;

/// <summary>
/// A single connected client, independent of the transport (TCP, future QUIC,
/// loopback for tests, ...). Tracks the protocol <see cref="State"/> the
/// connection has progressed to and knows how to push a message back to it.
/// </summary>
public abstract class NetClient {
    public ulong Id { get; }
    public ConnectionState State { get; set; } = ConnectionState.Handshaking;

    /// <summary>
    /// The protocol version this connection speaks. Starts at the registry default and
    /// is reassigned once the handshake announces a version, so the same listener serves
    /// clients on different (modern) versions.
    /// </summary>
    public Protocol Protocol { get; set; }

    /// <summary>The player name for this connection, set at login (modern Login Start / legacy Handshake).</summary>
    public string? PlayerName { get; set; }

    /// <summary>
    /// Whether this client is a spawned, IN-WORLD player — i.e. should receive world streaming and
    /// entity-visibility broadcasts. A logged-in client (modern OR legacy) is in the
    /// <see cref="ConnectionState.Play"/> state; broadcast predicates test this rather than the raw enum.
    /// </summary>
    public bool InWorld => State == ConnectionState.Play;

    protected NetClient(ulong id, Protocol protocol) {
        Id = id;
        Protocol = protocol;
    }

    /// <summary>Encodes and sends a single message to this client.</summary>
    public abstract void Send(IMessage message);

    /// <summary>Sends a broadcast packet, reusing the framed bytes cached for this client's version.</summary>
    public abstract void Send(CachedPacket packet);

    /// <summary>Closes the underlying connection.</summary>
    public abstract void Disconnect();

    /// <summary>
    /// Switches this connection's transport to AES/CFB8 stream encryption (legacy 1.5.2 login). All
    /// traffic after the call is encrypted with the shared secret (used as both key and IV). No-op for
    /// transports that don't encrypt (test doubles).
    /// </summary>
    public virtual void EnableEncryption(byte[] sharedSecret) { }
}
