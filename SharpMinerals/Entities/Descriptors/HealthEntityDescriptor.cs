using SharpMinerals.Entities.Components;

namespace SharpMinerals.Entities.Descriptors;

/// <summary>
/// Definition component (lives on an <see cref="EntityType"/> flyweight, not on an ECS entity):
/// marks an entity kind as living and carries its max health. Spawn factories read it to seed the
/// per-instance <see cref="HealthEntityComponent"/> component. Parallels <see cref="HealthEntityComponent"/> (current, instance)
/// vs this (max, definition).
/// </summary>
public sealed record HealthEntityDescriptor(float MaxHealth);
