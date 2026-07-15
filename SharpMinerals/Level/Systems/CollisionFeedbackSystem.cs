using Arch.Core;
using SharpMinerals.Entities.Components;
using ArchEntity = Arch.Core.Entity;

namespace SharpMinerals.Level.Systems;

/// <summary>Entity-vs-entity overlap: each entity with a <see cref="CollisionEntityComponent"/> box
/// records the dropped items it overlaps (read by item pickup). Candidates come from the
/// <see cref="SpatialIndex"/>, so only nearby items are tested.</summary>
public sealed class CollisionFeedbackSystem : ITickable {
    static readonly QueryDescription ColliderQuery = new QueryDescription().WithAll<TransformEntityComponent, CollisionEntityComponent, InteractionReachEntityComponent>();

    // Upper bound on pickup reach; bounds which chunk-cubes the index scans (the box test below decides membership).
    const double QueryRadius = 3.0;

    readonly World world;
    readonly List<ArchEntity> candidates = []; // reused scratch buffer; no per-tick alloc

    public CollisionFeedbackSystem(World world) => this.world = world;

    public void Tick() {
        var index = world.Entities;
        var ecs = world.Ecs;

        world.Ecs.Query(in ColliderQuery, (ArchEntity self, ref TransformEntityComponent t, ref CollisionEntityComponent c, ref InteractionReachEntityComponent reachBox) => {
            c.Touching.Clear(); // never null: Player.Spawn always constructs the list

            candidates.Clear();
            index.Near(t.X, t.Y, t.Z, QueryRadius, candidates);
            if (candidates.Count == 0) return;

            double tx = t.X, ty = t.Y, tz = t.Z, hw = reachBox.HalfWidth, h = reachBox.Height;
            foreach (var other in candidates) {
                if (other == self || !ecs.Has<PickupEntityComponent>(other)) continue; // only items
                var it = ecs.Get<TransformEntityComponent>(other);
                var ib = ecs.Get<HitboxEntityComponent>(other); // the item's physical box
                double reach = hw + ib.HalfWidth;
                if (System.Math.Abs(tx - it.X) <= reach && System.Math.Abs(tz - it.Z) <= reach
                    && it.Y >= ty - 1.0 && it.Y <= ty + h)
                    c.Touching.Add(other);
            }
        });
    }
}
