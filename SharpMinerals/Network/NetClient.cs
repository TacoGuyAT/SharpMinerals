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

    protected NetClient(ulong id, Protocol protocol) {
        Id = id;
        Protocol = protocol;
    }

    /// <summary>Encodes and sends a single message to this client.</summary>
    public abstract void Send(IMessage message);

    /// <summary>Closes the underlying connection.</summary>
    public abstract void Disconnect();
}
