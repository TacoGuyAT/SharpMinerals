using SharpMinerals.Chat;
using SharpMinerals.Commands;
using SharpMinerals.Network;
using SharpMinerals.Network.Messages;

namespace SharpMinerals.Entities.Components;

/// <summary>ECS component for any entity that participates in chat/commands. Implements <see cref="ISender"/>:
/// delivers messages to the backing connection (if any) and exposes its id as the command source.</summary>
public struct SenderEntityComponent : ISender {
    public string SenderName;
    /// <summary>The backing connection (chat delivery + the command source's player identity), injected when
    /// the entity is added. Null for a non-client sender (e.g. a mob), or before wiring — a safe no-op then.</summary>
    public NetClient? Client;

    public readonly string Name => SenderName;

    public readonly void ReceiveMessage(ChatComponent message) =>
        Client?.Send(new SystemChatMessageS2C(message, Overlay: false));

    public static SenderEntityComponent ForPlayer(string name) => new() { SenderName = name };
    public static SenderEntityComponent ForEntity(string name) => new() { SenderName = name };
}
