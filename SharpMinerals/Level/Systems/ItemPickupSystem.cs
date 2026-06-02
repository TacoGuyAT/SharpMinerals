using Arch.Core;
using SharpMinerals.Entities.Components;
using SharpMinerals.Events;
using ArchEntity = Arch.Core.Entity;

namespace SharpMinerals.Level.Systems;

/// <summary>Item pickup: each player whose collision-feedback box overlaps a pickable drop collects it and the
/// item despawns. Runs after <see cref="CollisionFeedbackSystem"/>. Client effects go out as deferred
/// <see cref="ItemPickedUp"/> events so networking runs on the server thread.</summary>
public sealed class ItemPickupSystem : ITickable {
    static readonly QueryDescription CollectorQuery =
        new QueryDescription().WithAll<NetPlayerEntityComponent, CollisionFeedbackEntityComponent, InventoryEntityComponent>();

    readonly World world;
    // Collected during the query, processed after (mutating inventories/despawning mid-iteration is unsafe).
    readonly List<(ArchEntity Collector, ArchEntity Drop)> pending = new();

    public ItemPickupSystem(World world) => this.world = world;

    public void Tick() {
        var ecs = world.Ecs;
        pending.Clear();
        ecs.Query(in CollectorQuery, (ArchEntity self, ref CollisionFeedbackEntityComponent c) => {
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
            if (picked <= 0) continue; // inventory full — nothing taken

            int netId = pickup.EntityId;
            if (leftover.IsEmpty)
                world.DestroyEntity(drop);
            else
                pickup.Stack = leftover; // partial pickup — the rest stays on the ground

            world.Events?.PublishDeferred(new ItemPickedUp(world, collector, netId, picked, leftover));
        }
    }
}
