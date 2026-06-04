using SharpMinerals.Level;
using ArchEntity = Arch.Core.Entity;

namespace SharpMinerals.Events;

// Generic entity events - fire for ANY entity, not just players. Player events derive from these, so via
// polymorphic dispatch a Subscribe<EntityMoved> sees both items and players.

/// <summary>Raised after an entity's position changed. Carries the world + ECS entity handle; the base of
/// <see cref="PlayerMoved"/>.</summary>
public record EntityMoved(World World, ArchEntity Entity);
