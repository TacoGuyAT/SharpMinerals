using SharpMinerals.Blocks;
using SharpMinerals.Components;
using SharpMinerals.Entities;
using SharpMinerals.Entities.Components;
using SharpMinerals.Entities.Descriptors;
using SharpMinerals.Modding;
using ArchEntity = Arch.Core.Entity;

namespace SharpMinerals;

/// <summary>The engine's own built-in "mod": registers the engine primitives (air, missing) and the built-in
/// entity kinds (item, player, falling_block) through the SAME registration path as any content. With this, the
/// registries hold no special-cased engine content and need no <c>Engine()</c>/namespace-forcing helpers - the
/// registration sites live here, in a mod, not in the registries. <see cref="ModLoader"/> sets
/// <c>CurrentNamespace = "sharpminerals"</c> around <see cref="OnInitialize"/>, so everything here lands in the
/// engine namespace. The host loads this FIRST (before vanilla) so air gets palette id 0 and missing id 1.</summary>
[ModInfo("sharpminerals", "1.0.0", ["SharpMinerals"])]
public sealed class CoreMod : Mod {
    public override void OnInitialize() {
        // Engine COMPONENTS are not registered here: the component source generator emits a [ModuleInitializer]
        // that registers every [Component] type in this assembly (namespaced from the [ModInfo] above) at load.

        // Engine blocks first: air MUST be palette id 0, missing id 1 (the chunk store + FromState depend on it).
        BlockRegistry.Air = BlockRegistry.Register("air", isAir: true);
        BlockRegistry.Missing = BlockRegistry.Register("missing");

        // Built-in entity kinds. Each declares its ECS component recipe ONCE here via .Blueprint - the single
        // source of truth for "what makes an item/player/falling block". Components whose value is per-spawn
        // (transform, the carried stack/block, ...) get a placeholder the spawn factory overwrites; the blueprint
        // just guarantees they EXIST to overwrite.
        EntityRegistry.Item = EntityRegistry.Register("item").Blueprint(ecs => ecs.Create(
            new TransformEntityComponent(),
            new VelocityEntityComponent(0, 0, 0),
            // A dropped item collides with terrain but doesn't block placement (Physics, not Placement).
            new HitboxEntityComponent(0.25, 0.25, CollisionUsage.Physics),
            new GravityEntityComponent(),
            new PickupEntityComponent()))
            .Persist(); // dropped items are saved with the world

        EntityRegistry.Player = EntityRegistry.Register("player")
            .Add(new HealthEntityDescriptor(MaxHealth: 20f))
            .Blueprint(ecs => ecs.Create(
                new TransformEntityComponent(),
                new SyncedTransformEntityComponent(),
                new VelocityEntityComponent(0, 0, 0),
                new HealthEntityComponent(20f),
                new InventoryEntityComponent(),
                // The true 0.6x1.8 player hitbox, used to block placement. Players are physics-excluded
                // (client-driven), so it has no Physics usage.
                new HitboxEntityComponent(0.6, 1.8, CollisionUsage.Placement),
                // A wider proximity box for nearby interactions (item pickup today); larger than the hitbox.
                new InteractionReachEntityComponent(1.5, 2.0),
                // Object initializer, NOT a parameterless ctor - that's bypassed on Arch default-init paths.
                new CollisionEntityComponent { Touching = new List<ArchEntity>() },
                new ChunkViewEntityComponent(),
                new EquipmentEntityComponent(),
                new SenderEntityComponent(),
                new NetPlayerEntityComponent()));

        EntityRegistry.FallingBlock = EntityRegistry.Register("falling_block").Blueprint(ecs => ecs.Create(
            new TransformEntityComponent(),
            new VelocityEntityComponent(0, 0, 0),
            // A falling block collides with terrain (so it lands) AND blocks placement while it's mid-fall.
            new HitboxEntityComponent(0.98, 0.98, CollisionUsage.Physics | CollisionUsage.Placement),
            new GravityEntityComponent(),
            new BlockCollisionEntityComponent(),
            // Default to the "missing" block so a data-less spawn (e.g. /summon falling_block) still has a real
            // block to fall, render and re-place as; SpawnFallingBlock overwrites it with the actual block.
            new FallingBlockEntityComponent { Block = BlockRegistry.Missing }))
            .Persist(); // a falling block in flight is saved so its block isn't lost on shutdown
    }
}
