using Arch.Core;
using SharpMinerals.Entities.Components;
using SharpMinerals.Items;
using SharpMinerals.Network.Containers;
using SharpMinerals.Network.Messages;
using SharpMinerals.Network.Protocols.JE763;
using World = SharpMinerals.Level.World;

namespace SharpMinerals.Network.Handlers;

/// <summary>
/// Per-tick item-entity networking: announces newly-spawned dropped items to clients
/// (Spawn Entity + item metadata) and handles pickups when a player's
/// <see cref="CollisionFeedback"/> box overlaps a pickable drop — adding it to the
/// inventory, despawning the entity, and pushing the player's updated window.
/// </summary>
public static class DropSystem {
    const int ItemEntityType = 54; // minecraft:item

    static readonly QueryDescription DropQuery = new QueryDescription().WithAll<DroppedItem, Transform>();

    public static void Tick(Server server) {
        foreach (var world in server.Worlds.Values)
            AnnounceNewDrops(server, world);

        foreach (var (clientId, handle) in server.Players)
            Pickup(server, clientId, handle);
    }

    static void AnnounceNewDrops(Server server, World world) {
        var pending = new List<(Entity Entity, ItemStack Stack, double X, double Y, double Z)>();
        world.Ecs.Query(in DropQuery, (Entity e, ref DroppedItem d, ref Transform t) => {
            if (d.EntityId == 0 && !d.Stack.IsEmpty) pending.Add((e, d.Stack, t.X, t.Y, t.Z));
        });

        foreach (var (entity, stack, x, y, z) in pending) {
            int id = server.NextEntityId();
            world.Ecs.Get<DroppedItem>(entity).EntityId = id;
            // Bundle the spawn + its item data so the client never sees a contents-less item.
            Broadcast(server, new BundleDelimiterS2C());
            Broadcast(server, new SpawnEntityS2C(
                EntityId: id, Uuid: Guid.NewGuid(), Type: ItemEntityType,
                X: x, Y: y, Z: z, Pitch: 0, Yaw: 0, HeadYaw: 0, Data: 0,
                VelocityX: 0, VelocityY: 0, VelocityZ: 0));
            Broadcast(server, new SetItemEntityMetadataS2C(id, VanillaMapping.ItemId(stack.Type!), stack.Count));
            Broadcast(server, new BundleDelimiterS2C());
        }
    }

    static void Pickup(Server server, ulong clientId, Server.PlayerHandle handle) {
        var ecs = handle.World.Ecs;
        if (!ecs.IsAlive(handle.Entity) || !ecs.Has<CollisionFeedback>(handle.Entity))
            return;

        var touching = ecs.Get<CollisionFeedback>(handle.Entity).Touching;
        if (touching.Count == 0) return;

        var inventory = ecs.Get<EntityInventory>(handle.Entity);
        bool changed = false;

        foreach (var other in touching.ToArray()) {
            if (!ecs.IsAlive(other) || !ecs.Has<DroppedItem>(other)) continue;
            ref var drop = ref ecs.Get<DroppedItem>(other);
            if (drop.PickupDelay > 0 || drop.EntityId == 0) continue;

            var leftover = inventory.Add(drop.Stack);
            changed = true;
            if (leftover.IsEmpty) {
                Broadcast(server, new RemoveEntitiesS2C(new[] { drop.EntityId }));
                ecs.Destroy(other);
            } else {
                drop.Stack = leftover; // partial pickup — the rest stays on the ground
            }
        }

        if (changed)
            server.NetServer.Send(clientId, new SetContainerContentS2C(0, 0, ContainerManager.PlayerWindow(inventory), default));
    }

    static void Broadcast(Server server, IMessage message) =>
        server.NetServer.Broadcast(message, c => c.State == ConnectionState.Play);
}
