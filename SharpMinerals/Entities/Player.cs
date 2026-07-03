using SharpMinerals.Entities.Components;
using SharpMinerals.Items;
using SharpMinerals.Level;
using SharpMinerals.Persistence;
using ArchEntity = Arch.Core.Entity;

namespace SharpMinerals.Entities;

/// <summary>Prefab/factory for the components that make up a player, keeping the engine component-oriented.</summary>
public static class Player {
    public static ArchEntity Spawn(World world, ulong clientId, string name, Guid uuid, int entityId, TransformEntityComponent pos, byte[]? saved = null) {
        // Spawn the blueprint at the default point; a returning player's saved blob then overwrites the persistent
        // components (placement, health, inventory) generically via EntityCodec. A new player keeps the blueprint's
        // full health and gets a starter kit.
        var entity = world.Spawn(CoreMod.Player, pos);
        var ecs = world.Ecs;

        if (saved is { } blob) {
            EntityCodec.Apply(ecs, entity, blob);
            // The restored transform moved the entity off its spawn cell - re-file it in the spatial index, which
            // world.Spawn registered at `spawn`.
            var restored = ecs.Get<TransformEntityComponent>(entity);
            world.Entities.Update(entity, restored.X, restored.Y, restored.Z);
        } else {
            var inventory = new InventoryEntityComponent();
            if(ItemType.TryFromPath("minecraft:stone", out var stone))
                inventory.Main(0) = new ItemStack(stone, 64);
            if(ItemType.TryFromPath("minecraft:chest", out var chest))
                inventory.Main(1) = new ItemStack(chest, 64);

            ecs.Get<InventoryEntityComponent>(entity) = inventory;
        }

        // Per-instance/session components the blueprint can't fill. Seed the movement-relay baseline to the FINAL
        // transform (restored or default) so a freshly-joined player doesn't immediately re-broadcast it.
        var transform = ecs.Get<TransformEntityComponent>(entity);
        ecs.Get<NetTransformEntityComponent>(entity) = new NetTransformEntityComponent {
            Position = transform.Position,
            Yaw = transform.Yaw, 
            Pitch = transform.Pitch
        };
        ecs.Get<SenderEntityComponent>(entity) = SenderEntityComponent.ForPlayer(name);
        ecs.Get<PlayerEntityComponent>(entity) = new PlayerEntityComponent { 
            ClientId = clientId, 
            Name = name, 
            Uuid = uuid, 
            NetId = entityId, 
            GameMode = CoreMod.Survival 
        };
        return entity;
    }
}
