using SharpMinerals.Blocks;
using SharpMinerals.Items;
using SharpMinerals.Level;
using SharpMinerals.Math;
using ArchEntity = Arch.Core.Entity;

namespace SharpMinerals.Events;

// Events the world-based simulation systems publish DEFERRED for the networking layer to act on. The
// systems run on the parallel world-tick threads and only mutate world state; the matching client
// packets are sent by server-thread subscribers when the bus drains (same pattern as EntityMoved).

/// <summary>Raised after an <c>ItemPickupSystem</c> moved a dropped item into a collector's inventory. The
/// item entity has already been despawned (or its remaining stack reduced); the subscriber broadcasts the
/// collect animation + entity removal/metadata and resyncs the collector's window.</summary>
public record ItemPickedUp(World World, ArchEntity Collector, int PickupNetId, int Count, ItemStack Leftover);

/// <summary>Raised after a <c>FallingBlockSystem</c> landed a falling block: the entity is despawned and,
/// when the resting cell was free, <see cref="PlacedBlock"/> was set into the world (null if it instead
/// popped as an item). The subscriber broadcasts the block change (if any) and removes the entity.</summary>
public record FallingBlockLanded(World World, int NetId, Vector3i Cell, BlockType? PlacedBlock);
