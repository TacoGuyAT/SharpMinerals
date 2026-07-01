using Arch.Core;
using SharpMinerals.Entities.Components;
using SharpMinerals.Items;
using SharpMinerals.Network;
using SharpMinerals.Network.Containers;
using SharpMinerals.Network.Messages;
using ArchEntity = Arch.Core.Entity;

namespace SharpMinerals.Level.Systems;

/// <summary>Item pickup: each player whose collision-feedback box overlaps a pickable drop collects it and the
/// item despawns. Runs after <see cref="CollisionFeedbackSystem"/>. The collect animation, the drop's removal,
/// and the collector's window resync are projected to clients in <see cref="Flush"/> after the tick.</summary>
public sealed class ItemPickupSystem : ITickable, INetworkSystem {
    static readonly QueryDescription CollectorQuery =
        new QueryDescription().WithAll<PlayerEntityComponent, CollisionEntityComponent, InventoryEntityComponent>();

    readonly World world;
    // Collected during the query, processed after (mutating inventories/despawning mid-iteration is unsafe).
    readonly List<(ArchEntity Collector, ArchEntity Drop)> pending = new();
    readonly List<(ArchEntity Collector, ArchEntity Drop, int NetId, int Count, ItemStack Leftover)> collected = new(); // Tick -> Flush hand-off

    public ItemPickupSystem(World world) => this.world = world;

    public void Tick() {
        var ecs = world.Ecs;
        pending.Clear();
        ecs.Query(in CollectorQuery, (ArchEntity self, ref CollisionEntityComponent c) => {
            foreach (var other in c.Touching)
                if (ecs.IsAlive(other) && ecs.Has<PickupEntityComponent>(other))
                    pending.Add((self, other));
        });

        foreach (var (collector, drop) in pending) {
            if (!ecs.IsAlive(collector) || !ecs.IsAlive(drop)) continue; // a drop touched by two players is taken once
            ref var pickup = ref ecs.Get<PickupEntityComponent>(drop);
            if (pickup.PickupDelay > 0 || pickup.EntityId == 0) continue; // not yet pickable / not yet announced

            var inventory = ecs.Get<InventoryEntityComponent>(collector);
            int original = pickup.Stack.Count;
            var leftover = inventory.Add(pickup.Stack);
            int picked = original - leftover.Count;
            if (picked <= 0) continue; // inventory full - nothing taken

            // Claim the drop now (emptied on a full pickup, shrunk on a partial) so a second collector this tick
            // skips it - but DON'T despawn it yet: the entity tracker despawns synchronously on DestroyEntity, and
            // the collect animation must reach the client BEFORE the removal. The destroy happens in Flush.
            int netId = pickup.EntityId;
            pickup.Stack = leftover;
            collected.Add((collector, drop, netId, picked, leftover));
        }
    }

    /// <summary>Post-tick: play the collect animation, remove/shrink the ground stack, and resync the
    /// collector's window. Others see a newly-held item via the equipment diff later in the same flush.</summary>
    public void Flush(Server server) {
        if (collected.Count == 0) return;
        var ecs = world.Ecs;
        foreach (var (collector, drop, netId, count, leftover) in collected) {
            // Collect animation (item flies to the collector) - sent FIRST, while the client still has the entity.
            if (ecs.IsAlive(collector))
                Broadcast(server, new CollectItemS2C(netId, ecs.Get<PlayerEntityComponent>(collector).NetId, count));

            // Then update the ground stack: a full pickup despawns the drop (the tracker's RemoveEntities now follows
            // the collect animation); a partial one just pushes the new count.
            if (leftover.IsEmpty) {
                if (ecs.IsAlive(drop)) world.DestroyEntity(drop);
            } else {
                Broadcast(server, new SetItemEntityMetadataS2C(netId, leftover));
            }

            if (ecs.IsAlive(collector) && ecs.Get<SenderEntityComponent>(collector).Client is { } client) {
                server.NetServer.Send(client.Id, new SetContainerContentS2C(0, 0, server.Containers.PlayerWindow(client.Id).BuildSnapshot(), default));
            }
        }
        collected.Clear();
    }

    static void Broadcast(Server server, IMessage message) =>
        server.NetServer.Broadcast(message, c => c.State == ConnectionState.Play);
}
