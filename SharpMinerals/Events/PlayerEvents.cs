using SharpMinerals.Entities.Components;
using SharpMinerals.Events.Contexts;
using SharpMinerals.Level;
using SharpMinerals.Network;
using ArchEntity = Arch.Core.Entity;

namespace SharpMinerals.Events;

/// <summary>
/// Base for player events, handled via polymorphic dispatch.
/// </summary>
public record PlayerEvent(PlayerContext Context) {
    public Server Server => Context.Server;
    public World World => Context.World;
    public ArchEntity Entity => Context.Entity;
    public NetClient Client => Context.Client;
    public NetPlayerEntityComponent GetPlayer() => Context.GetPlayer();
}

/// <summary>
/// Raised after a player has fully joined and entered the Play state.
/// </summary>
public sealed record PlayerJoined(PlayerContext Context) : PlayerEvent(Context);

/// <summary>
/// Raised when a player leaves, just before its entity is removed.
/// </summary>
public sealed record PlayerLeft(PlayerContext Context) : PlayerEvent(Context);
