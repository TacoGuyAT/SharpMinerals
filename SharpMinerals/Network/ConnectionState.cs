namespace SharpMinerals.Network;

/// <summary>
/// The protocol state a connection is in; starts at <see cref="Handshaking"/>.
/// Packet ids are only unique within a (state, direction) pair.
/// </summary>
public enum ConnectionState {
    Handshaking = 0,
    Status = 1,
    Login = 2,
    Configuration = 3,
    Play = 4,
}

public enum PacketDirection {
    Serverbound,
    Clientbound,
}
