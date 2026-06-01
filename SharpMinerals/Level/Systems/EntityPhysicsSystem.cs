using Arch.Core;
using SharpMinerals.Entities.Components;
using SharpMinerals.Events;
using SharpMinerals.Math;
using ArchEntity = Arch.Core.Entity;

namespace SharpMinerals.Level.Systems;

/// <summary>
/// Server-simulated physics for non-player entities (dropped items, falling blocks; future mobs/projectiles):
/// velocity integration swept against solid terrain, gravity, then drag. The downward pull is applied only
/// to entities carrying a <see cref="GravityEntityComponent"/> (opt-in), and an entity with a
/// <see cref="BlockCollisionFeedbackEntityComponent"/> gets its ground contact recorded there. Players are
/// client-driven and deliberately excluded (<c>WithNone&lt;NetPlayerEntityComponent&gt;</c>).
/// </summary>
public sealed class EntityPhysicsSystem : ITickable {
    // Physics tuning (per tick). Gravity pulls Y down; air drag bleeds off velocity in flight, and the
    // stronger ground friction stops an entity's horizontal slide soon after it lands.
    const double Gravity = 0.04;
    const double Drag = 0.98;
    const double GroundFriction = 0.6;
    const double CollisionEpsilon = 1e-4;
    // Below this per-tick displacement an entity is treated as at rest — no move event (so a settled
    // item asymptotically creeping to a stop doesn't spam EntityMoved every tick).
    const double MoveEpsilon = 1e-3;

    static readonly QueryDescription PhysicsQuery =
        new QueryDescription().WithAll<TransformEntityComponent, VelocityEntityComponent, ColliderEntityComponent>().WithNone<NetPlayerEntityComponent>();

    readonly World world;

    public EntityPhysicsSystem(World world) => this.world = world;

    public void Tick() {
        world.Ecs.Query(in PhysicsQuery, (ArchEntity e, ref TransformEntityComponent t, ref VelocityEntityComponent v, ref ColliderEntityComponent box) => {
            double ox = t.X, oy = t.Y, oz = t.Z;
            if (world.Ecs.Has<GravityEntityComponent>(e)) v.Y -= Gravity; // opt-in downward pull
            bool onGround = MoveWithCollision(ref t, ref v, box);
            double horizontalDrag = onGround ? GroundFriction : Drag;
            v.X *= horizontalDrag;
            v.Y *= Drag;
            v.Z *= horizontalDrag;

            // Record block-collision feedback (ground contact) for entities that read it (falling blocks).
            if (world.Ecs.Has<BlockCollisionFeedbackEntityComponent>(e))
                world.Ecs.Get<BlockCollisionFeedbackEntityComponent>(e).OnGround = onGround;

            // Any entity (item today, mobs/projectiles later) raises a generic move event when it
            // actually shifts. Deferred: worlds tick on parallel threads, so this enqueues onto the
            // bus and the subscribers run on the single tick-writer thread (next DrainDeferred).
            if (Moved(ox, oy, oz, in t))
                world.Events?.PublishDeferred(new EntityMoved(world, e));
        });
    }

    static bool Moved(double ox, double oy, double oz, in TransformEntityComponent t) =>
        System.Math.Abs(t.X - ox) > MoveEpsilon
        || System.Math.Abs(t.Y - oy) > MoveEpsilon
        || System.Math.Abs(t.Z - oz) > MoveEpsilon;

    /// <summary>
    /// Integrates <paramref name="v"/> into <paramref name="t"/> one axis at a time, sweeping the
    /// entity's AABB against solid blocks. On a hit the box is snapped flush to the block face and
    /// that axis's velocity is zeroed. Axis-separated resolution keeps it simple and stable for the
    /// thin boxes we have today; Y is resolved first so the horizontal axes settle on solid ground.
    /// Returns true if the entity landed on a floor this step (used to apply ground friction).
    /// </summary>
    bool MoveWithCollision(ref TransformEntityComponent t, ref VelocityEntityComponent v, in ColliderEntityComponent box) {
        double hw = box.HalfWidth, h = box.Height;
        bool onGround = false;

        double newY = t.Y + v.Y;
        if (v.Y != 0 && BoxHitsSolid(t.X - hw, newY, t.Z - hw, t.X + hw, newY + h, t.Z + hw)) {
            if (v.Y < 0) { newY = System.Math.Floor(newY) + 1.0; onGround = true; } // land on top of the block
            else newY = System.Math.Floor(newY + h) - h;                            // bonk a ceiling
            v.Y = 0;
        }
        t.Y = newY;

        double newX = t.X + v.X;
        if (v.X != 0 && BoxHitsSolid(newX - hw, t.Y, t.Z - hw, newX + hw, t.Y + h, t.Z + hw)) {
            newX = v.X > 0 ? System.Math.Floor(newX + hw) - hw - CollisionEpsilon
                           : System.Math.Floor(newX - hw) + 1.0 + hw + CollisionEpsilon;
            v.X = 0;
        }
        t.X = newX;

        double newZ = t.Z + v.Z;
        if (v.Z != 0 && BoxHitsSolid(t.X - hw, t.Y, newZ - hw, t.X + hw, t.Y + h, newZ + hw)) {
            newZ = v.Z > 0 ? System.Math.Floor(newZ + hw) - hw - CollisionEpsilon
                           : System.Math.Floor(newZ - hw) + 1.0 + hw + CollisionEpsilon;
            v.Z = 0;
        }
        t.Z = newZ;

        return onGround;
    }

    /// <summary>True if the axis-aligned box spanning [min,max] overlaps any non-air block.</summary>
    bool BoxHitsSolid(double minX, double minY, double minZ, double maxX, double maxY, double maxZ) {
        int x0 = (int)System.Math.Floor(minX), x1 = (int)System.Math.Floor(maxX - CollisionEpsilon);
        int y0 = (int)System.Math.Floor(minY), y1 = (int)System.Math.Floor(maxY - CollisionEpsilon);
        int z0 = (int)System.Math.Floor(minZ), z1 = (int)System.Math.Floor(maxZ - CollisionEpsilon);
        for (int x = x0; x <= x1; x++)
            for (int y = y0; y <= y1; y++)
                for (int z = z0; z <= z1; z++)
                    if (!world.GetBlock(new Vector3i(x, y, z)).IsAir) return true;
        return false;
    }
}
