using SharpMinerals.Chat;

namespace SharpMinerals.Network.Messages;

/// <summary>Client begins login with its name and (since 1.19.1) a profile UUID.</summary>
public sealed record LoginStartC2S(string Name, Guid ProfileId) : IMessage;

/// <summary>
/// Server refuses or terminates a login, carrying a chat component reason.
/// </summary>
public sealed record LoginDisconnectS2C(ChatComponent Reason) : IMessage;

/// <summary>
/// Server accepts the login (offline mode — no encryption/authentication). After
/// this the connection switches to the Play state.
/// </summary>
public sealed record LoginSuccessS2C(Guid Uuid, string Name) : IMessage;
