using SharpMinerals.Entities.Components;

namespace SharpMinerals.Entities;

/// <summary>
/// A snapshot of the persistent parts of a player entity — placement (position + rotation),
/// health, and inventory — captured on disconnect and restored on reconnect. Per-session data
/// (network ids, velocity, collision feedback, chat routing) is intentionally excluded.
/// Stored by player UUID through an <c>IPlayerStore</c>: the in-memory store keeps the snapshot
/// live, the RocksDB store serializes it via <c>PlayerStateCodec</c>.
/// </summary>
public readonly record struct PlayerState(TransformEntityComponent Transform, HealthEntityComponent Health, InventoryEntityComponent Inventory);
