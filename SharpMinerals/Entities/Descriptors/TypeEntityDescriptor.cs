namespace SharpMinerals.Entities.Descriptors;

/// <summary>ECS component tagging an entity with its flyweight <see cref="EntityType"/> definition, so a
/// system (e.g. the network layer picking a spawn message) can ask "what kind is this?".</summary>
[Component]
public struct TypeEntityDescriptor {
    public EntityType Type;
}
