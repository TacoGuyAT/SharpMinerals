using SharpMinerals.Events.Contexts;

namespace SharpMinerals.Events;

// Player lifecycle events. Reference types (records) so they can share bases/interfaces — PlayerMoved
// derives from the generic EntityMoved (see EntityEvents) and PlayerInventoryChanged from
// EntityInventoryChanged, so player handlers are a special case of the entity ones.

/// <summary>Raised after a player has fully joined and entered the Play state.</summary>
public sealed record PlayerJoined(PlayerContext Context);

/// <summary>Raised after a player's position and/or rotation changed. An <see cref="EntityMoved"/>
/// carrying the player's context, so generic entity-move handlers and player-move handlers both see it
/// from one publish.</summary>
public sealed record PlayerMoved(PlayerContext Context)
    : EntityMoved(Context.World, Context.Entity);

/// <summary>Raised when a player leaves, just before its entity is removed.</summary>
public sealed record PlayerLeft(PlayerContext Context);

/// <summary>Raised after a player's inventory contents or selected hotbar slot changed — pickup, container
/// click, creative set, or a toss. An <see cref="EntityInventoryChanged"/> whose entity is a player, so
/// the equipment-visibility subscriber (and any entity-inventory handler) sees it from one publish.</summary>
public sealed record PlayerInventoryChanged(PlayerContext PlayerContext)
    : EntityInventoryChanged(PlayerContext);
