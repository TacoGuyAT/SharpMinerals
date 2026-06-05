using SharpMinerals.Entities.Components;
using SharpMinerals.Items;
using SharpMinerals.Level;
using SharpMinerals.Persistence;
using ArchEntity = Arch.Core.Entity;

namespace SharpMinerals.Entities;

/// <summary>Prefab/factory for the components that make up a player, keeping the engine component-oriented.</summary>
public static class Player {
    /// <summary>The player kind's max health (from its <c>Living</c> definition component).</summary>
    public static float MaxHealth => EntityRegistry.Player.MaxHealth;

    public static ArchEntity Spawn(World world, ulong clientId, string name, Guid uuid, int entityId, TransformEntityComponent spawn, byte[]? saved = null) {
        // Spawn the blueprint at the default point; a returning player's saved blob then overwrites the persistent
        // components (placement, health, inventory) generically via EntityCodec. A new player keeps the blueprint's
        // full health and gets a starter kit.
        var entity = world.Spawn(EntityRegistry.Player, spawn);
        var ecs = world.Ecs;

        if (saved is { } blob) {
            EntityCodec.Apply(ecs, entity, blob);
            // The restored transform moved the entity off its spawn cell - re-file it in the spatial index, which
            // world.Spawn registered at `spawn`.
            var restored = ecs.Get<TransformEntityComponent>(entity);
            world.Entities.Update(entity, restored.X, restored.Y, restored.Z);
        } else {
            ecs.Get<InventoryEntityComponent>(entity) = NewInventory();
        }

        // Per-instance/session components the blueprint can't fill. Seed the movement-relay baseline to the FINAL
        // transform (restored or default) so a freshly-joined player doesn't immediately re-broadcast it.
        var transform = ecs.Get<TransformEntityComponent>(entity);
        ecs.Get<NetTransformEntityComponent>(entity) = new NetTransformEntityComponent {
            Position = transform.Position, Yaw = transform.Yaw, Pitch = transform.Pitch };
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
