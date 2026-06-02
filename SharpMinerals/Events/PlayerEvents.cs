using SharpMinerals.Events.Contexts;

namespace SharpMinerals.Events;

// Player lifecycle events. Derive from the generic entity events, so player handlers are a special case of
// the entity ones under polymorphic dispatch.

/// <summary>Raised after a player has fully joined and entered the Play state.</summary>
public sealed record PlayerJoined(PlayerContext Context);

/// <summary>Raised when a player leaves, just before its entity is removed.</summary>
public sealed record PlayerLeft(PlayerContext Context);
