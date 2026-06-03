using SharpMinerals.Components;
using SharpMinerals.Entities.Descriptors;

namespace SharpMinerals.Entities;

/// <summary>A registered entity kind — a flyweight definition (one shared instance per kind) assembled from
/// components, like <see cref="Items.ItemType"/>/<see cref="Blocks.BlockType"/>. The wire id is a per-version
/// concern resolved by <see cref="Network.ITypeMapper.EntityTypeId"/>, not stored here; reference identity holds.</summary>
public sealed class EntityType : ComponentObject {
    internal int TypeId { get; }

    /// <summary>The namespaced identifier (e.g. <c>minecraft:falling_block</c>).</summary>
    public Identifier Id { get; }

    internal EntityType(int id, Identifier identifier) {
        TypeId = id;
        Id = identifier;
    }

    /// <summary>Max health from a <see cref="HealthEntityDescriptor"/> component (0 if this kind isn't living, e.g. an item).</summary>
    public float MaxHealth => TryGet<HealthEntityDescriptor>(out var l) ? l.MaxHealth : 0f;

    public override string ToString() => Id.Name;
}
