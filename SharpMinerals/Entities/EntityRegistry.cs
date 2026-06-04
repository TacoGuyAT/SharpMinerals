using SharpMinerals.Blocks;
using SharpMinerals.Components;
using SharpMinerals.Entities.Components;
using SharpMinerals.Entities.Descriptors;
using SharpMinerals.Items;
using SharpMinerals.Modding;
using ArchEntity = Arch.Core.Entity;

namespace SharpMinerals.Entities;

/// <summary>The entity registry - flyweight <see cref="EntityType"/> definitions assembled from components,
/// mirroring <c>BlockRegistry</c>/<c>ItemRegistry</c>. Ids are assigned in registration order.</summary>
public static class EntityRegistry {
    static readonly List<EntityType> byId = [];
    static readonly Dictionary<string, EntityType> byIdentifier = []; // keyed by full namespaced Id
    static bool frozen;

    // Engine = built-in (forces the sharpminerals namespace - these are engine entities, not vanilla content, but
    // the type mapper still maps them to vanilla wire ids: sharpminerals:item->54, falling_block->36, player->spawn).
    // Register = mod (current namespace). Forcing avoids the ambient-namespace trap at static-init time (see BlockRegistry).
    static EntityType Engine(string name) => Add(Identifier.EngineNamespace, name);

    static EntityType Add(string ns, string name) {
        if (frozen)
            throw new InvalidOperationException(
                $"EntityRegistry is frozen - register entity \"{name}\" during mod OnInitialize.");
        var identifier = new Identifier(ns, name);
        string key = identifier.Full;
        if (byIdentifier.ContainsKey(key))
            throw new ArgumentException($"An entity \"{key}\" is already registered.", nameof(name));
        var type = new EntityType(byId.Count, identifier);
        byId.Add(type);
        byIdentifier[key] = type;
        return type;
    }

    /// <summary>Registers a new entity kind, returning it for fluent composition (<c>.Add(...)</c>).
    /// For mods - call from <c>Mod.OnInitialize</c>; throws once <see cref="Freeze">frozen</see>. Namespaced under
    /// the loading mod's id. A modded kind needs a wire id in the type mapper before it can be spawned to clients.</summary>
    public static EntityType Register(string name) => Add(ModContent.CurrentNamespace, name);

    /// <summary>Seals the registry - the host calls this after mods init, before protocols are built.</summary>
    public static void Freeze() => frozen = true;

    // Each built-in kind declares its ECS component recipe ONCE here via .Blueprint - the single source of truth
    // for "what makes an item/player/falling block". World.Spawn invokes the blueprint, tags it with the type, sets
    // the per-instance components (transform, ...) and registers it in the spatial index. Components whose value is
    // per-spawn (transform, velocity, health, inventory, the networked identity, the carried stack/block) get a
    // placeholder default here that the factory overwrites; the blueprint just guarantees they EXIST to overwrite.
    public static readonly EntityType Item = Engine("item").Blueprint(ecs => ecs.Create(
        new TransformEntityComponent(),
        new VelocityEntityComponent(0, 0, 0),
        // A dropped item collides with terrain but doesn't block placement (Physics, not Placement).
        new HitboxEntityComponent(0.25, 0.25, CollisionUsage.Physics),
        new GravityEntityComponent(),
        new PickupEntityComponent())
    );

    public static readonly EntityType Player = Engine("player")
        .Add(new HealthEntityDescriptor(MaxHealth: 20f))
        .Blueprint(ecs => ecs.Create(
            new TransformEntityComponent(),
            new SyncedTransformEntityComponent(),
            new VelocityEntityComponent(0, 0, 0),
            new HealthEntityComponent(20f),
            new InventoryEntityComponent(),
            // The true 0.6x1.8 player hitbox, used to block placement (a block can't go where a player stands).
            // Players are physics-excluded (client-driven), so it has no Physics usage.
            new HitboxEntityComponent(0.6, 1.8, CollisionUsage.Placement),
            // A wider proximity box for nearby interactions (item pickup today); larger than the hitbox.
            new InteractionReachEntityComponent(1.5, 2.0),
            // Object initializer, NOT a parameterless ctor - that's bypassed on Arch default-init paths.
            new CollisionEntityComponent { Touching = new List<ArchEntity>() },
            new ChunkViewEntityComponent(),
            new EquipmentEntityComponent(),
            new SenderEntityComponent(),
            new NetPlayerEntityComponent())
        );

    public static readonly EntityType FallingBlock = Engine("falling_block").Blueprint(ecs => ecs.Create(
        new TransformEntityComponent(),
        new VelocityEntityComponent(0, 0, 0),
        // A falling block collides with terrain (so it lands) AND blocks placement while it's mid-fall.
        new HitboxEntityComponent(0.98, 0.98, CollisionUsage.Physics | CollisionUsage.Placement),
        new GravityEntityComponent(),
        new BlockCollisionEntityComponent(),
        // Default to the "missing" block so a data-less spawn (e.g. /summon falling_block) still has a real block
        // to fall, render and re-place as; SpawnFallingBlock overwrites it with the actual block.
        new FallingBlockEntityComponent { Block = BlockRegistry.Missing })
    );

    public static IReadOnlyList<EntityType> All => byId;
    public static EntityType FromId(int id) => byId[id];

    /// <summary>The entity kind for <paramref name="id"/> - a bare path (defaults to <c>minecraft:</c>) or a
    /// full <c>namespace:path</c> - or null if unregistered.</summary>
    public static EntityType? FromName(string id) => byIdentifier.GetValueOrDefault(ItemRegistry.Normalize(id));
}
