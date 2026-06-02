using SharpMinerals.Events.Contexts;

namespace SharpMinerals.Events;

// Player lifecycle events. Derive from the generic entity events, so player handlers are a special case of
// the entity ones under polymorphic dispatch.

/// <summary>Raised after a player has fully joined and entered the Play state.</summary>
public sealed record PlayerJoined(PlayerContext Context);

/// <summary>Raised after a player's position and/or rotation changed. An <see cref="EntityMoved"/>, so
/// entity-move and player-move handlers both see it.</summary>
public sealed record PlayerMoved(PlayerContext Context)
    : EntityMoved(Context.World, Context.Entity);

/// <summary>Raised when a player leaves, just before its entity is removed.</summary>
public sealed record PlayerLeft(PlayerContext Context);

/// <summary>Raised after a player's inventory contents or selected hotbar slot changed. An
/// <see cref="EntityInventoryChanged"/>, so the equipment-visibility subscriber and any entity-inventory
/// handler both see it.</summary>
public sealed record PlayerInventoryChanged(PlayerContext PlayerContext)
    : EntityInventoryChanged(PlayerContext);
