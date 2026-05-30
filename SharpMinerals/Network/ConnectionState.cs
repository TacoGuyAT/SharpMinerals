namespace SharpMinerals.Network;

/// <summary>
/// The protocol state a connection is in. A connection starts in
/// <see cref="Handshaking"/> and transitions as packets are exchanged.
/// Packet ids are only unique within a (state, direction) pair.
/// See https://minecraft.wiki/w/Java_Edition_protocol#Packet_format.
/// </summary>
public enum ConnectionState {
    Handshaking = 0,
    Status = 1,
    Login = 2,
    Configuration = 3,
    Play = 4,
}

/// <summary>Which way a packet travels relative to the server.</summary>
public enum PacketDirection {
    /// <summary>Client → server.</summary>
    Serverbound,
    /// <summary>Server → client.</summary>
    Clientbound,
}
