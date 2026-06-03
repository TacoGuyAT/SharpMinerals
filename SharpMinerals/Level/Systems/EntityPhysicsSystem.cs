using Arch.Core;
using SharpMinerals.Entities.Components;
using SharpMinerals.Events;
using SharpMinerals.Math;
using ArchEntity = Arch.Core.Entity;

namespace SharpMinerals.Level.Systems;

/// <summary>Server-simulated physics for entities whose hitbox opts in with <see cref="CollisionUsage.Physics"/>
/// (items, falling blocks): velocity integration swept against terrain, gravity (opt-in via
/// <see cref="GravityEntityComponent"/>), then drag. Players are client-driven so their hitbox has no Physics
/// usage and is skipped. Ground contact is recorded on any <see cref="BlockCollisionEntityComponent"/>.</summary>
public sealed class EntityPhysicsSystem : ITickable {
    // Physics tuning, per tick.
    const double Gravity = 0.04;
    const double Drag = 0.98;
    const double GroundFriction = 0.6;
    const double CollisionEpsilon = 1e-4;
    // Below this per-tick displacement an entity is at rest — no move event (else a settling item spams it).
    const double MoveEpsilon = 1e-3;

    static readonly QueryDescription PhysicsQuery =
        new QueryDescription().WithAll<TransformEntityComponent, VelocityEntityComponent, HitboxEntityComponent>();

    readonly World world;

    public EntityPhysicsSystem(World world) => this.world = world;

    public void Tick() {
        world.Ecs.Query(in PhysicsQuery, (ArchEntity e, ref TransformEntityComponent t, ref VelocityEntityComponent v, ref HitboxEntityComponent box) => {
            if (!box.Usage.HasFlag(CollisionUsage.Physics)) return; // e.g. a player's hitbox (client-driven)
            double ox = t.X, oy = t.Y, oz = t.Z;
            if (world.Ecs.Has<GravityEntityComponent>(e)) v.Y -= Gravity;
            bool onGround = MoveWithCollision(ref t, ref v, box);
            double horizontalDrag = onGround ? GroundFriction : Drag;
            v.X *= horizontalDrag;
            v.Y *= Drag;
            v.Z *= horizontalDrag;

            if (world.Ecs.Has<BlockCollisionEntityComponent>(e))
                world.Ecs.Get<BlockCollisionEntityComponent>(e).OnGround = onGround;

            // Deferred: worlds tick on parallel threads, so subscribers run on the tick-writer thread.
            if (Moved(ox, oy, oz, in t))
                world.Events?.PublishDeferred(new EntityMoved(world, e));
        });
    }

    static bool Moved(double ox, double oy, double oz, in TransformEntityComponent t) =>
        System.Math.Abs(t.X - ox) > MoveEpsilon
        || System.Math.Abs(t.Y - oy) > MoveEpsilon
        || System.Math.Abs(t.Z - oz) > MoveEpsilon;

    /// <summary>Integrates velocity into the transform one axis at a time, sweeping the AABB against solid
    /// blocks; on a hit the box snaps flush and that axis's velocity zeroes. Y is resolved first so the
    /// horizontal axes settle on solid ground. Returns true if the entity landed on a floor this step.</summary>
    bool MoveWithCollision(ref TransformEntityComponent t, ref VelocityEntityComponent v, in HitboxEntityComponent box) {
        double hw = box.HalfWidth, h = box.Height;
        bool onGround = false;

        double newY = t.Y + v.Y;
        if (v.Y != 0 && BoxHitsSolid(t.X - hw, newY, t.Z - hw, t.X + hw, newY + h, t.Z + hw)) {
            if (v.Y < 0) { newY = System.Math.Floor(newY) + 1.0; onGround = true; } // land on top
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
