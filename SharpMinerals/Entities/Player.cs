using SharpMinerals.Blocks;
using SharpMinerals.Entities.Components;
using SharpMinerals.Entities.Descriptors;
using SharpMinerals.Items;
using SharpMinerals.Level;
using ArchEntity = Arch.Core.Entity;

namespace SharpMinerals.Entities;

/// <summary>
/// Prefab/factory for the set of components that make up a player. Keeping the
/// "what is a player" knowledge here (rather than in a subclass) is what lets the
/// rest of the engine stay component-oriented.
/// </summary>
public static class Player {
    /// <summary>The player kind's max health (from its <c>Living</c> definition component).</summary>
    public static float MaxHealth => EntityRegistry.Player.MaxHealth;

    public static ArchEntity Spawn(World world, ulong clientId, string name, Guid uuid, int entityId, TransformEntityComponent spawn, PlayerState? saved = null) {
        // A returning player restores their saved placement/health/inventory; a new one gets the
        // default spawn point, full health and a starter kit.
        var transform = saved?.Transform ?? spawn;
        var health = saved?.Health ?? new HealthEntityComponent(MaxHealth);
        var inventory = saved?.Inventory ?? NewInventory();

        return world.Ecs.Create(
            transform,
            new VelocityEntityComponent(0, 0, 0),
            health,
            inventory,
            // Players are excluded from the physics step (their position is client-driven), so this
            // box currently serves only as the item-pickup reach for CollisionFeedback. Real player
            // terrain-collision dimensions come with the deferred player-physics work.
            new ColliderEntityComponent(1.5, 2.0),
            // Object initializer (a plain field assignment), NOT a parameterless ctor — the latter is
            // bypassed on default-init paths (Arch arrays / Release JIT), which left Touching null.
            new CollisionFeedbackEntityComponent { Touching = new List<ArchEntity>() },
            new ChunkViewEntityComponent(),
            new TypeEntityDescriptor { Type = EntityRegistry.Player },
            SenderEntityComponent.ForPlayer(name),
            new NetPlayerEntityComponent { ClientId = clientId, Name = name, Uuid = uuid, EntityId = entityId },
            // Tracks the equipment others have been shown; the first InventoryChanged at join fills it.
            new EquipmentEntityComponent());
    }

    static InventoryEntityComponent NewInventory() {
        var inventory = new InventoryEntityComponent();
        inventory.Main(0) = new ItemStack(BlockRegistry.Stone, 64);
        inventory.Main(1) = new ItemStack(BlockRegistry.Chest, 64);
        return inventory;
    }
}
