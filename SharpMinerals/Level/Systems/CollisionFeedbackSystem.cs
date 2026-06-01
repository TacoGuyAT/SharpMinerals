using Arch.Core;
using SharpMinerals.Entities.Components;
using ArchEntity = Arch.Core.Entity;

namespace SharpMinerals.Level.Systems;

/// <summary>
/// Entity-vs-entity overlap: each entity with a <see cref="CollisionFeedbackEntityComponent"/> box records the
/// dropped items it overlaps (read by item pickup). Both boxes come from the <see cref="ColliderEntityComponent"/>.
/// Candidates come from the world's <see cref="SpatialIndex"/> — only items in the few chunk-cubes
/// around each collider are tested, instead of scanning every item in the world.
/// </summary>
public sealed class CollisionFeedbackSystem : ITickable {
    static readonly QueryDescription ColliderQuery = new QueryDescription().WithAll<TransformEntityComponent, CollisionFeedbackEntityComponent, ColliderEntityComponent>();

    // Safe upper bound on pickup reach: a collider's box can extend ~1 block below and up to its
    // height above, so this sphere comfortably contains every box-valid item. The exact box test below
    // still decides membership — this only bounds which chunk-cubes the index scans.
    const double QueryRadius = 3.0;

    readonly World world;
    // Reused scratch buffer for the per-collider index query (cleared each use; no per-tick alloc).
    readonly List<ArchEntity> candidates = new();

    public CollisionFeedbackSystem(World world) => this.world = world;

    public void Tick() {
        var index = world.Entities;
        var ecs = world.Ecs;

        world.Ecs.Query(in ColliderQuery, (ArchEntity self, ref TransformEntityComponent t, ref CollisionFeedbackEntityComponent c, ref ColliderEntityComponent box) => {
            c.Touching.Clear(); // never null: Player.Spawn always constructs the list, on the (single) tick thread

            candidates.Clear();
            index.Near(t.X, t.Y, t.Z, QueryRadius, candidates);
            if (candidates.Count == 0) return;

            double tx = t.X, ty = t.Y, tz = t.Z, hw = box.HalfWidth, h = box.Height;
            foreach (var other in candidates) {
                if (other == self || !ecs.Has<PickupEntityComponent>(other)) continue; // only items, never self
                var it = ecs.Get<TransformEntityComponent>(other);
                var ib = ecs.Get<ColliderEntityComponent>(other);
                double reach = hw + ib.HalfWidth;
                if (System.Math.Abs(tx - it.X) <= reach && System.Math.Abs(tz - it.Z) <= reach
                    && it.Y >= ty - 1.0 && it.Y <= ty + h)
                    c.Touching.Add(other);
            }
        });
    }
}
