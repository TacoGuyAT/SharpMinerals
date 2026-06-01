using Arch.Core;
using SharpMinerals.Entities.Components;

namespace SharpMinerals.Level.Systems;

/// <summary>Ages each dropped item and ticks down its pickup delay. The pickup itself lives in
/// <see cref="ItemPickupSystem"/> (which runs later in the world tick once the delay has elapsed).</summary>
public sealed class ItemLifecycleSystem : ITickable {
    static readonly QueryDescription ItemLifecycleQuery = new QueryDescription().WithAll<PickupEntityComponent>();

    readonly World world;

    public ItemLifecycleSystem(World world) => this.world = world;

    public void Tick() {
        world.Ecs.Query(in ItemLifecycleQuery, (ref PickupEntityComponent drop) => {
            drop.Age++;
            if (drop.PickupDelay > 0) drop.PickupDelay--;
        });
    }
}
