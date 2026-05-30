namespace SharpMinerals.Network.Messages;

/// <summary>
/// The first packet a client sends. Its <see cref="NextState"/> tells the server
/// whether the connection is a status ping (1) or a login attempt (2).
/// </summary>
public sealed record HandshakeC2S(
    int ProtocolVersion,
    string ServerAddress,
    ushort ServerPort,
    int NextState) : IMessage;
