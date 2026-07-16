using SharpMinerals.Blocks;
using SharpMinerals.Entities;
using SharpMinerals.Entities.Components;
using SharpMinerals.Modding;

namespace SharpMinerals;

/// <summary>The engine's own built-in "mod": registers the engine primitives (air, missing) and the built-in
/// entity kinds (item, player, falling_block) through the SAME registration path as any content. With this, the
/// registries hold no special-cased engine content and need no <c>Engine()</c>/namespace-forcing helpers - the
/// registration sites live here, in a mod, not in the registries. <see cref="ModLoader"/> sets
/// <c>CurrentNamespace = "sharpminerals"</c> around <see cref="OnInitialize"/>, so everything here lands in the
/// engine namespace. The host loads this FIRST (before vanilla) so air gets palette id 0 and missing id 1.</summary>
[ModInfo("sharpminerals", "1.0.0", ["SharpMinerals"])]
public sealed class CoreMod : Mod {
    public static GameMode Creative { get; internal set; } = null!;
    public static GameMode Survival { get; internal set; } = null!;
    public static GameMode Adventure { get; internal set; } = null!;

    /// <summary>The empty cell (palette id 0). The chunk store and <see cref="FromState"/> depend on this id.
    /// Registered by <see cref="CoreMod"/> (the engine mod, loaded first) - non-null after engine init.</summary>
    public static BlockType Air { get; internal set; } = null!;

    /// <summary>Placeholder for content the server can't represent (a dropped mod block, an unmappable type). It
    /// renders as stone on the wire (the type mapper's fallback) but is a distinct, non-air block. Registered by
    /// <see cref="CoreMod"/>.</summary>
    public static BlockType Missing { get; internal set; } = null!;

    public static EntityType Item { get; internal set; } = null!;
    public static EntityType Player { get; internal set; } = null!;
    public static EntityType FallingBlock { get; internal set; } = null!;

    public override void OnInitialize() {
        // Engine COMPONENTS are not registered here: the component source generator emits a [ModuleInitializer]
        // that registers every [Component] type in this assembly (namespaced from the [ModInfo] above) at load.

        Creative = GameMode.Register("creative", 
            PlayerFlags.CreativeMode | 
            PlayerFlags.CanBreakBlocks | 
            PlayerFlags.CanPlaceBlocks | 
            PlayerFlags.InstantBreak | 
            PlayerFlags.HasCollision | 
            PlayerFlags.CanFly | 
            PlayerFlags.Invulnerable
        );

        Survival = GameMode.Register("survival",
            PlayerFlags.CanBreakBlocks |
            PlayerFlags.CanPlaceBlocks |
            PlayerFlags.HasCollision |
            PlayerFlags.CanTakeDamage
        );

        // No build rights (maps to client gamemode 2, see GameMode.IntoId): the client blocks edits visually
        // and the dig/place handlers refuse them server-side. The protection mode for curated worlds (lobby).
        Adventure = GameMode.Register("adventure",
            PlayerFlags.HasCollision |
            PlayerFlags.CanTakeDamage
        );

        // Engine blocks first: air MUST be palette id 0, missing id 1 (the chunk store + FromState depend on it).
        Air = BlockType.Register("air", isAir: true);
        Missing = BlockType.Register("missing");

        // Built-in entity kinds. Each declares its ECS component recipe ONCE here via .Blueprint - the single
        // source of truth for "what makes an item/player/falling block". Components whose value is per-spawn
        // (transform, the carried stack/block, ...) get a placeholder the spawn factory overwrites; the blueprint
        // just guarantees they EXIST to overwrite.
        Item = EntityType.Register("item").Blueprint(ecs => ecs.Create(
            new TransformEntityComponent(),
            new VelocityEntityComponent(0, 0, 0),
            // A dropped item collides with terrain but doesn't block placement (Physics, not Placement).
            new HitboxEntityComponent(0.25, 0.25, CollisionUsage.Physics),
            new GravityEntityComponent(),
            new PickupEntityComponent()
        )).Persist(); // dropped items are saved with the world

        Player = EntityType.Register("player") .Blueprint(ecs => ecs.Create(
            new TransformEntityComponent(),
            new NetTransformEntityComponent(),
            new VelocityEntityComponent(0, 0, 0),
            new HealthEntityComponent(20f),
            new InventoryEntityComponent(),
            // The true 0.6x1.8 player hitbox, used to block placement. Players are physics-excluded
            // (client-driven), so it has no Physics usage.
            new HitboxEntityComponent(0.6, 1.8, CollisionUsage.Placement),
            // A wider proximity box for nearby interactions (item pickup today); larger than the hitbox.
            new InteractionReachEntityComponent(1.5, 2.0),
            // Its Touching list is a per-tick scratch buffer that CollisionFeedbackSystem lazily creates.
            new CollisionEntityComponent(),
            new ChunkViewEntityComponent(),
            new EquipmentEntityComponent(),
            new EntityTrackerComponent(), // per-player view: which entities its client currently has spawned
            new SenderEntityComponent(),
            new PlayerEntityComponent(),
            new StateEntityComponent(),
            new DiggingEntityComponent()
        ));

        FallingBlock = EntityType.Register("falling_block").Blueprint(ecs => ecs.Create(
            new TransformEntityComponent(),
            new VelocityEntityComponent(0, 0, 0),
            // A falling block collides with terrain (so it lands) AND blocks placement while it's mid-fall.
            new HitboxEntityComponent(0.98, 0.98, CollisionUsage.Physics | CollisionUsage.Placement),
            new GravityEntityComponent(),
            new BlockCollisionEntityComponent(),
            // Default to the "missing" block so a data-less spawn (e.g. /summon falling_block) still has a real
            // block to fall, render and re-place as; SpawnFallingBlock overwrites it with the actual block.
            new FallingBlockEntityComponent { Block = Missing }
        )).Persist(); // a falling block in flight is saved so its block isn't lost on shutdown
    }
}
