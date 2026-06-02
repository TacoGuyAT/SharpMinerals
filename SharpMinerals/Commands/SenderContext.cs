using System.Diagnostics.CodeAnalysis;
using SharpMinerals.Chat;
using SharpMinerals.Level;
using SharpMinerals.Network;
using ArchEntity = Arch.Core.Entity;

namespace SharpMinerals.Commands;

/// <summary>
/// The command source (Brigadier's <c>TSource</c>, like Minecraft's <c>CommandSourceStack</c>). Wraps the
/// <see cref="ISender"/> that receives output, the <see cref="CommandDispatcher"/>, and the issuing player's
/// connection (<see cref="Client"/>, null for a non-player sender).
/// <para/>
/// The player's world+entity are resolved live via <see cref="TryGetEntity"/>, never snapshotted, so a cached
/// re-executed parse always acts on the current entity (surviving respawns/world switches).
/// </summary>
public sealed class SenderContext {
    public ISender Sender { get; }
    public CommandDispatcher Dispatcher { get; }
    public NetClient? Client { get; }

    public Server Server => Dispatcher.Server;
    /// <summary>Whether a player issued this command (i.e. it has an in-world entity).</summary>
    public bool IsPlayer => Client is not null;

    public SenderContext(ISender sender, CommandDispatcher dispatcher, NetClient? client = null) {
        Sender = sender;
        Dispatcher = dispatcher;
        Client = client;
    }

    public void Reply(string text) => Sender.ReceiveMessage(new TextComponent(text));
    public void Reply(ChatComponent message) => Sender.ReceiveMessage(message);

    /// <summary>Resolves the issuing player's current world and entity (false for the console, or a
    /// dropped/despawned player). Looked up live on every call.</summary>
    public bool TryGetEntity([MaybeNullWhen(false)] out World world, out ArchEntity entity) {
        world = null;
        entity = default;
        if (Client is null || !Server.TryGetPlayer(Client.Id, out var player) || !player.World.Ecs.IsAlive(player.Entity))
            return false;
        world = player.World;
        entity = player.Entity;
        return true;
    }
}
