using SharpMinerals.Chat;
using SharpMinerals.Commands;
using SharpMinerals.Network.Messages;

namespace SharpMinerals.Entities.Components;

/// <summary>
/// ECS component for any entity that participates in chat/commands — a player, but
/// also e.g. a command-issuing mob. The struct implements <see cref="IChatSender"/>
/// directly: it receives messages (delivered to the backing client, if any) and can
/// issue commands on its own via <see cref="RunCommand"/>.
/// </summary>
public struct ChatSender : IChatSender {
    public string SenderName;
    /// <summary>Backing client connection id, or 0 for a non-client sender (e.g. a mob).</summary>
    public ulong ClientId;

    public readonly string Name => SenderName;

    public readonly void SendMessage(ChatComponent message) {
        if (ClientId != 0)
            Server.Instance?.NetServer.Send(ClientId, new SystemChatMessageS2C(message, Overlay: false));
        // A non-client sender has nowhere to deliver chat; it can still issue commands.
    }

    /// <summary>Runs a command as this sender, so an entity can drive the server itself.</summary>
    public readonly void RunCommand(string command) {
        var dispatcher = Server.Instance?.Commands;
        if (dispatcher is not null)
            _ = dispatcher.ExecuteAsync(this, command);
    }

    public static ChatSender ForPlayer(ulong clientId, string name) => new() { ClientId = clientId, SenderName = name };
    public static ChatSender ForEntity(string name) => new() { SenderName = name };
}
