using System.Diagnostics.CodeAnalysis;
using SharpMinerals.Chat;
using SharpMinerals.Level;
using SharpMinerals.Network;
using ArchEntity = Arch.Core.Entity;

namespace SharpMinerals.Commands;

/// <summary>
/// The command source — Brigadier's <c>TSource</c>, the analogue of Minecraft's <c>CommandSourceStack</c>.
/// Wraps the <see cref="ISender"/> that receives output, the <see cref="CommandDispatcher"/> (for server
/// access and sub-dispatch), and the issuing player's connection (<see cref="Client"/>, null for the console
/// or a non-player sender).
/// <para/>
/// The player's world+entity are resolved LIVE from the connection on demand (<see cref="TryGetEntity"/>),
/// never snapshotted — so a cached, re-executed parse always acts on the player's current entity, surviving
/// respawns and world switches. <c>.Requires(s =&gt; s.IsPlayer)</c> gates player-perspective commands.
/// </summary>
public sealed class SenderContext {
    public ISender Sender { get; }
    public CommandDispatcher Dispatcher { get; }
    /// <summary>The issuing player's connection, or null for the non-player sender.</summary>
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

    /// <summary>Resolves the issuing player's current world and entity — false for the console, or if the
    /// player has since dropped/despawned. Looked up live by connection id on every call (never cached).</summary>
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
