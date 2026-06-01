using SharpMinerals.Components;
using SharpMinerals.Entities.Descriptors;

namespace SharpMinerals.Entities;

/// <summary>
/// A registered entity kind — a flyweight definition (one shared instance per kind), assembled
/// from components, exactly like <see cref="Items.ItemType"/>/<see cref="Blocks.BlockType"/>.
/// Identity (<see cref="Id"/>, <see cref="Name"/>) is core; data such as max health lives in
/// components added via <c>With(...)</c>. The on-the-wire numeric id is a per-version network
/// concern resolved by <see cref="Network.ITypeMapper.EntityTypeId"/>, NOT stored here. Reference
/// identity (<c>== EntityRegistry.Player</c> holds).
/// </summary>
public sealed class EntityType : ComponentObject {
    public int Id { get; }
    public string Name { get; }

    internal EntityType(int id, string name) {
        Id = id;
        Name = name;
    }

    /// <summary>Max health from a <see cref="HealthEntityDescriptor"/> component (0 if this kind isn't living, e.g. an item).</summary>
    public float MaxHealth => TryGet<HealthEntityDescriptor>(out var l) ? l.MaxHealth : 0f;

    public override string ToString() => Name;
}
