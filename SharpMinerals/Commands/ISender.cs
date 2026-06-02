using SharpMinerals.Chat;

namespace SharpMinerals.Commands;

/// <summary>
/// Anything that issues commands/chat and receives messages — the server console, a
/// player, a command-issuing mob, etc. Submitted text is routed by the
/// <see cref="CommandDispatcher"/> (a leading <c>/</c> runs a command), and output
/// comes back through <see cref="ReceiveMessage"/>.
/// </summary>
public interface ISender {
    string Name { get; }

    void ReceiveMessage(ChatComponent message);
}
