using SharpMinerals.Blocks;
using SharpMinerals.Items;
using SharpMinerals.Level;
using SharpMinerals.Math;
using ArchEntity = Arch.Core.Entity;

namespace SharpMinerals.Events;

// Events the simulation systems publish DEFERRED for the networking layer. Systems run on parallel
// world-tick threads and only mutate world state; server-thread subscribers send the client packets on drain.

/// <summary>Raised after <c>ItemPickupSystem</c> moved a dropped item into a collector's inventory (the item
/// entity is already despawned or reduced). The subscriber broadcasts the collect + resyncs the window.</summary>
public record ItemPickedUp(World World, ArchEntity Collector, int PickupNetId, int Count, ItemStack Leftover);

/// <summary>Raised after <c>FallingBlockSystem</c> landed a falling block. <see cref="PlacedBlock"/> is the
/// re-placed block, or null if it popped as an item. The subscriber broadcasts the change and removes the entity.</summary>
public record FallingBlockLanded(World World, int NetId, Vector3i Cell, BlockType? PlacedBlock);
