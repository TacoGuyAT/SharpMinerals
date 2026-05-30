namespace SharpMinerals.Network.Messages;

/// <summary>Client asks for the server list entry. No fields.</summary>
public sealed record StatusRequestC2S : IMessage;

/// <summary>Server's JSON answer describing version, players and MOTD.</summary>
public sealed record StatusResponseS2C(string Json) : IMessage;

/// <summary>Client's latency probe; the payload is echoed back verbatim.</summary>
public sealed record PingRequestC2S(long Payload) : IMessage;

/// <summary>Echo of <see cref="PingRequestC2S.Payload"/>; closes the connection after.</summary>
public sealed record PongResponseS2C(long Payload) : IMessage;
