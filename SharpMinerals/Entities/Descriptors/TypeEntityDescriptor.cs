namespace SharpMinerals.Entities.Descriptors;

/// <summary>
/// ECS component tagging an entity with its flyweight <see cref="EntityType"/> definition, so a
/// system can ask "what kind is this?" generically (the network layer reads it to pick the spawn
/// message). Holds a shared definition reference, so it's a managed component.
/// </summary>
public struct TypeEntityDescriptor {
    public EntityType Type;
}
