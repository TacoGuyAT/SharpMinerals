using SharpMinerals.Chat;

namespace SharpMinerals.Commands;

/// <summary>
/// Anything that issues commands/chat and receives messages — the server console, a
/// player, a command-issuing mob, etc. Submitted text is routed by the
/// <see cref="CommandDispatcher"/> (a leading <c>/</c> runs a command), and output
/// comes back through <see cref="SendMessage"/>.
/// </summary>
public interface IChatSender {
    /// <summary>A display name for the sender (e.g. "Console" or a player name).</summary>
    string Name { get; }

    /// <summary>Delivers a message to this sender (feedback, chat, command output).</summary>
    void SendMessage(ChatComponent message);
}
