namespace SharpMinerals.Network;

/// <summary>
/// A single connected client, independent of transport. Tracks the connection
/// <see cref="State"/> and pushes messages back to it.
/// </summary>
public abstract class NetClient {
    public ulong Id { get; }
    public ConnectionState State { get; set; } = ConnectionState.Handshaking;

    /// <summary>
    /// The protocol version this connection speaks. Starts at the registry default,
    /// reassigned once the handshake announces a version.
    /// </summary>
    public Protocol Protocol { get; set; }

    public string? Name { get; set; }

    /// <summary>
    /// Whether this client is a spawned, in-world player that should receive world streaming and
    /// entity-visibility broadcasts. Broadcast predicates test this rather than the raw enum.
    /// </summary>
    public bool InWorld => State == ConnectionState.Play;

    protected NetClient(ulong id, Protocol protocol) {
        Id = id;
        Protocol = protocol;
    }

    public abstract void Send(IMessage message);

    /// <summary>Reuses the framed bytes cached for this client's version.</summary>
    public abstract void Send(CachedPacket packet);

    public abstract void Disconnect();

    /// <summary>
    /// Switches the transport to AES/CFB8 stream encryption (legacy 1.5.2 login); shared secret is
    /// used as both key and IV. No-op for transports that don't encrypt.
    /// </summary>
    public virtual void EnableEncryption(byte[] sharedSecret) { }
}
