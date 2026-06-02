namespace SharpMinerals.Network.Messages;

public sealed record StatusRequestC2S : IMessage;

public sealed record StatusResponseS2C(string Json) : IMessage;

public sealed record PingRequestC2S(long Payload) : IMessage;

/// <summary>Closes the connection after sending.</summary>
public sealed record PongResponseS2C(long Payload) : IMessage;
