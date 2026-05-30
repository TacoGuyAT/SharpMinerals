using SharpMinerals.Blocks;
using SharpMinerals.Entities.Components;
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
    public const float MaxHealth = 20f;

    public static ArchEntity Spawn(World world, ulong clientId, string name, Guid uuid, int entityId, Transform transform) {
        var inventory = new EntityInventory();
        inventory.Main(0) = new ItemStack(BlockRegistry.Stone, 64);
        inventory.Main(1) = new ItemStack(BlockRegistry.Chest, 64);

        return world.Ecs.Create(
            transform,
            new Velocity(0, 0, 0),
            new Health(MaxHealth),
            inventory,
            new CollisionFeedback(1.5, 2.0),
            ChatSender.ForPlayer(clientId, name),
            new NetworkedPlayer { ClientId = clientId, Name = name, Uuid = uuid, EntityId = entityId });
    }
}
