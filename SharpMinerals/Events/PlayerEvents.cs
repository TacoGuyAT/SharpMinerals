using SharpMinerals.Entities.Components;
using SharpMinerals.Events.Contexts;
using SharpMinerals.Level;
using SharpMinerals.Network;
using ArchEntity = Arch.Core.Entity;

namespace SharpMinerals.Events;

/// <summary>
/// Base for player events, handled via polymorphic dispatch.
/// </summary>
public record PlayerEvent {
    readonly PlayerContext context;
    public PlayerEvent(PlayerContext ctx) {
        context = ctx;
    }

    public Server Server => context.Server;
    public World World => context.World;
    public ArchEntity Entity => context.Entity;
    public NetClient Client => context.Client;
    public NetPlayerEntityComponent Player => context.Player;
}

/// <summary>
/// Raised after a player has fully joined and entered the Play state.
/// </summary>
public sealed record PlayerJoined(PlayerContext Context) : PlayerEvent(Context);

/// <summary>
/// Raised when a player leaves, just before its entity is removed.
/// </summary>
public sealed record PlayerLeft(PlayerContext Context) : PlayerEvent(Context);
