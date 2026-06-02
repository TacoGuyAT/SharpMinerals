using SharpMinerals.Chat;

namespace SharpMinerals.Network.Messages;

/// <summary>ProfileId added in 1.19.1.</summary>
public sealed record LoginStartC2S(string Name, Guid ProfileId) : IMessage;

public sealed record LoginDisconnectS2C(ChatComponent Reason) : IMessage;

/// <summary>Offline mode; the connection then switches to Play.</summary>
public sealed record LoginSuccessS2C(Guid Uuid, string Name) : IMessage;
