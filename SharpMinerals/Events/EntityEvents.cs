using SharpMinerals.Events.Contexts;
using SharpMinerals.Level;
using ArchEntity = Arch.Core.Entity;

namespace SharpMinerals.Events;

// Generic entity events — fire for ANY entity (dropped items, future mobs/projectiles), not just
// players. Player-specific events (see PlayerEvents) derive from these, so a Subscribe<EntityMoved>
// sees both a wandering item and a walking player via the bus's polymorphic dispatch.

/// <summary>
/// Raised after an entity's position changed. Carries the world + ECS entity handle; subscribers read
/// the current <c>Transform</c> from the world. The base of <see cref="PlayerMoved"/> — a player move
/// reaches both <c>Subscribe&lt;PlayerMoved&gt;</c> and <c>Subscribe&lt;EntityMoved&gt;</c>.
/// </summary>
public record EntityMoved(World World, ArchEntity Entity);

/// <summary>Base of the inventory-change hierarchy. Subscribe to hear EVERY inventory change — an entity's
/// or (later) a block container's — via the bus's polymorphic dispatch. Abstract: only the concrete
/// derivations are published.</summary>
public abstract record InventoryChanged;

/// <summary>Raised after an entity's inventory contents or selection changed. Carries the entity's
/// <see cref="EntityContext"/>; the base of <see cref="PlayerInventoryChanged"/>.</summary>
public record EntityInventoryChanged(EntityContext Context) : InventoryChanged;
