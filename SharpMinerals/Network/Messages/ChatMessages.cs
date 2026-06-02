using SharpMinerals.Chat;

namespace SharpMinerals.Network.Messages;

/// <summary>System Chat Message (0x64): server text in the chat box (or action bar when <paramref name="Overlay"/> is true).</summary>
public sealed record SystemChatMessageS2C(ChatComponent Content, bool Overlay) : IMessage;

/// <summary>Set Tab List Header And Footer (0x65): text above/below the player list; an empty component blanks a line.</summary>
public sealed record PlayerListHeaderFooterS2C(ChatComponent Header, ChatComponent Footer) : IMessage;

/// <summary>Player chat message (0x05). Only the text is read; the signing data is ignored.</summary>
public sealed record ChatMessageC2S(string Message) : IMessage;

/// <summary>Player slash-command (0x04). The command is sent without the leading '/'.</summary>
public sealed record ChatCommandC2S(string Command) : IMessage;
