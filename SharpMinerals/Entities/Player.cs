using SharpMinerals.Blocks;
using SharpMinerals.Entities.Components;
using SharpMinerals.Entities.Descriptors;
using SharpMinerals.Items;
using SharpMinerals.Level;
using ArchEntity = Arch.Core.Entity;

namespace SharpMinerals.Entities;

/// <summary>Prefab/factory for the components that make up a player, keeping the engine component-oriented.</summary>
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
            // Players are physics-excluded (client-driven); this box only serves item-pickup reach.
            new ColliderEntityComponent(1.5, 2.0),
            // Object initializer, NOT a parameterless ctor — that's bypassed on Arch default-init paths.
            new CollisionFeedbackEntityComponent { Touching = new List<ArchEntity>() },
            new ChunkViewEntityComponent(),
            new TypeEntityDescriptor { Type = EntityRegistry.Player },
            SenderEntityComponent.ForPlayer(name),
            new NetPlayerEntityComponent { ClientId = clientId, Name = name, Uuid = uuid, EntityId = entityId },
            new EquipmentEntityComponent());
    }

    static InventoryEntityComponent NewInventory() {
        var inventory = new InventoryEntityComponent();
        inventory.Main(0) = new ItemStack(BlockRegistry.Stone, 64);
        inventory.Main(1) = new ItemStack(BlockRegistry.Chest, 64);
        return inventory;
    }
}
