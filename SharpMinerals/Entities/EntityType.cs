using SharpMinerals.Entities.Descriptors;
using ArchWorld = Arch.Core.World;
using ArchEntity = Arch.Core.Entity;
using System.Diagnostics.CodeAnalysis;

namespace SharpMinerals.Entities;

/// <summary>A registered entity kind - a flyweight definition (one shared instance per kind) assembled from
/// components, like <see cref="Items.ItemType"/>/<see cref="Blocks.BlockType"/>. The wire id is a per-version
/// concern resolved by <see cref="Network.TypeMapper.EntityTypeId"/>, not stored here; reference identity holds.
/// A kind also carries a BLUEPRINT (set via <see cref="Blueprint"/>) - the recipe for its ECS components - so
/// <see cref="Create"/> is the single source of truth for "what makes a <c>player</c>/<c>item</c>/...".</summary>
public sealed class EntityType : ComponentObject {
    public static IReadOnlyList<EntityType> All => Registry.All;
    public static readonly Registry<EntityType> Registry = new();
    public static EntityType Register(string name) => Registry.Register(name, static (id, identifier) => new EntityType(id, identifier));
    public static bool TryFromPath(string path, [MaybeNullWhen(false)] out EntityType result) => Registry.TryFromPath(path, out result);

    internal int TypeId { get; }

    /// <summary>The namespaced identifier (e.g. <c>minecraft:falling_block</c>).</summary>
    public Identifier Id { get; }

    Func<ArchWorld, ArchEntity>? blueprint;

    internal EntityType(int id, Identifier identifier) {
        TypeId = id;
        Id = identifier;
    }

    /// <summary>Whether entities of this kind are saved with the world (e.g. dropped items). Off by default;
    /// players persist separately (by UUID) and transient kinds aren't worth storing.</summary>
    public bool Persisted { get; private set; }

    /// <summary>Marks this kind as world-persistent (saved/restored with the world). Fluent.</summary>
    public EntityType Persist(bool persisted = true) { Persisted = persisted; return this; }

    /// <summary>Sets the recipe for this kind's ECS components - a typed <c>ecs.Create(...)</c> of the entity's OWN
    /// components (NOT the <see cref="TypeEntityDescriptor"/> tag, which <see cref="Create"/> adds). Reference-bearing
    /// components must be constructed fresh here (the delegate runs once per spawn). Returns the type for chaining.</summary>
    public EntityType Blueprint(Func<ArchWorld, ArchEntity> create) {
        blueprint = create;
        return this;
    }

    /// <summary>Creates an ECS entity of this kind from its <see cref="Blueprint"/>, tagged with its
    /// <see cref="TypeEntityDescriptor"/>. The caller (typically <see cref="Level.World.Spawn"/>) then sets the
    /// per-instance components (transform, ...) and registers it in the spatial index.</summary>
    public ArchEntity Create(ArchWorld ecs) {
        var create = blueprint ?? throw new InvalidOperationException($"Entity type \"{Id.Full}\" has no blueprint.");
        var entity = create(ecs);
        ecs.Add(entity, new TypeEntityDescriptor { Type = this });
        return entity;
    }

    public override string ToString() => Id.Name;
}
