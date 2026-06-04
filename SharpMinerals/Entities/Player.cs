using SharpMinerals.Entities.Components;
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
        var entity = world.Spawn(EntityRegistry.Player, transform);

        // The blueprint (EntityRegistry.Player) laid down every component with placeholder defaults; here we set
        // only the ones that are per-instance. Everything else (hitbox, reach, collision list, ...) is already correct.
        var ecs = world.Ecs;
        // Seed the movement-relay baseline to spawn, so a freshly-joined player doesn't re-broadcast it.
        ecs.Get<SyncedTransformEntityComponent>(entity) = new SyncedTransformEntityComponent {
            X = transform.X, Y = transform.Y, Z = transform.Z, Yaw = transform.Yaw, Pitch = transform.Pitch };
        ecs.Get<HealthEntityComponent>(entity) = saved?.Health ?? new HealthEntityComponent(MaxHealth);
        ecs.Get<InventoryEntityComponent>(entity) = saved?.Inventory ?? NewInventory();
        ecs.Get<SenderEntityComponent>(entity) = SenderEntityComponent.ForPlayer(name);
        ecs.Get<NetPlayerEntityComponent>(entity) = new NetPlayerEntityComponent { ClientId = clientId, Name = name, Uuid = uuid, EntityId = entityId };
        return entity;
    }

    static InventoryEntityComponent NewInventory() {
        // Starter kit is vanilla content (registered by the minecraft mod) - resolve by name so the core doesn't
        // depend on the mod, and a server without vanilla content just spawns players empty-handed.
        var inventory = new InventoryEntityComponent();
        if (ItemRegistry.FromName("minecraft:stone") is { } stone) inventory.Main(0) = new ItemStack(stone, 64);
        if (ItemRegistry.FromName("minecraft:chest") is { } chest) inventory.Main(1) = new ItemStack(chest, 64);
        return inventory;
    }
}
