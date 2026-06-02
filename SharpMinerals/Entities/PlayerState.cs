using SharpMinerals.Entities.Components;

namespace SharpMinerals.Entities;

/// <summary>A snapshot of the persistent parts of a player entity (placement, health, inventory), captured
/// on disconnect and restored on reconnect. Per-session data is excluded. Stored by UUID via <c>IPlayerStore</c>.</summary>
public readonly record struct PlayerState(TransformEntityComponent Transform, HealthEntityComponent Health, InventoryEntityComponent Inventory);
