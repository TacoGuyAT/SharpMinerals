using Arch.Core;
using SharpMinerals.Entities.Components;
using SharpMinerals.Events;
using ArchEntity = Arch.Core.Entity;

namespace SharpMinerals.Level.Systems;

/// <summary>
/// Item pickup as world-based entity collision: each player whose <see cref="CollisionFeedbackEntityComponent"/>
/// box overlaps a pickable dropped item collects it into their inventory and the item entity despawns. Runs
/// after <see cref="CollisionFeedbackSystem"/> (which fills the overlap set). The client effects — collect
/// animation, entity removal/metadata, window resync — are emitted as deferred <see cref="ItemPickedUp"/>
/// events so the networking runs on the server thread, off the parallel world tick.
/// </summary>
public sealed class ItemPickupSystem : ITickable {
    static readonly QueryDescription CollectorQuery =
        new QueryDescription().WithAll<NetPlayerEntityComponent, CollisionFeedbackEntityComponent, InventoryEntityComponent>();

    readonly World world;
    // Collected during the query, processed after — we mutate inventories and despawn entities, which
    // must not happen during iteration.
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
