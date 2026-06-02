using SharpMinerals.Events.Contexts;
using SharpMinerals.Level;
using ArchEntity = Arch.Core.Entity;

namespace SharpMinerals.Events;

// Generic entity events — fire for ANY entity, not just players. Player events derive from these, so via
// polymorphic dispatch a Subscribe<EntityMoved> sees both items and players.

/// <summary>Raised after an entity's position changed. Carries the world + ECS entity handle; the base of
/// <see cref="PlayerMoved"/>.</summary>
public record EntityMoved(World World, ArchEntity Entity);

/// <summary>Base of the inventory-change hierarchy — subscribe to hear every inventory change. Abstract:
/// only the concrete derivations are published.</summary>
public abstract record InventoryChanged;

/// <summary>Raised after an entity's inventory contents or selection changed; the base of
/// <see cref="PlayerInventoryChanged"/>.</summary>
public record EntityInventoryChanged(EntityContext Context) : InventoryChanged;
