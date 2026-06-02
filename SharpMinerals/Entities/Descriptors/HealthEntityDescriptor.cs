using SharpMinerals.Entities.Components;

namespace SharpMinerals.Entities.Descriptors;

/// <summary>Definition component (lives on an <see cref="EntityType"/> flyweight): marks an entity kind as
/// living and carries its max health, which spawn factories read to seed the per-instance
/// <see cref="HealthEntityComponent"/>.</summary>
public sealed record HealthEntityDescriptor(float MaxHealth);
