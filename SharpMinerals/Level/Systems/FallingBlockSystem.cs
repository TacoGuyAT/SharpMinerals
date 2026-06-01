using Arch.Core;
using SharpMinerals.Blocks;
using SharpMinerals.Entities.Components;
using SharpMinerals.Events;
using SharpMinerals.Items;
using SharpMinerals.Math;
using ArchEntity = Arch.Core.Entity;

namespace SharpMinerals.Level.Systems;

/// <summary>
/// Lands falling blocks (sand/gravel) using world block-collision feedback: when a falling-block entity
/// is resting on the ground (<see cref="BlockCollisionFeedbackEntityComponent.OnGround"/>, set by
/// <see cref="EntityPhysicsSystem"/>), its carried block is placed back into the world — or popped as an
/// item if the resting cell is occupied — and the entity despawns. The fall itself is generic gravity +
/// collision; the client mirrors it. The client effects (block update, entity removal) are emitted as a
/// deferred <see cref="FallingBlockLanded"/> event so networking runs on the server thread.
/// </summary>
public sealed class FallingBlockSystem : ITickable {
    static readonly QueryDescription FallingQuery =
        new QueryDescription().WithAll<FallingBlockEntityComponent, TransformEntityComponent, BlockCollisionFeedbackEntityComponent>();

    readonly World world;
    // Collected during the query, applied after — placing blocks and despawning are not safe mid-iteration.
    readonly List<(ArchEntity Entity, int NetId, BlockType Block, Vector3i Cell)> landed = new();

    public FallingBlockSystem(World world) => this.world = world;

    public void Tick() {
        var ecs = world.Ecs;
        landed.Clear();
        ecs.Query(in FallingQuery, (ArchEntity e, ref FallingBlockEntityComponent f, ref TransformEntityComponent t, ref BlockCollisionFeedbackEntityComponent fb) => {
            // Announced (so the client tracks it) and resting on the ground → it has landed.
            if (f.EntityId == 0 || !fb.OnGround) return;
            var cell = new Vector3i((int)System.Math.Floor(t.X), (int)System.Math.Floor(t.Y), (int)System.Math.Floor(t.Z));
            landed.Add((e, f.EntityId, f.Block, cell));
        });

        foreach (var (entity, netId, block, cell) in landed) {
            if (!ecs.IsAlive(entity)) continue;
            BlockType? placed = null;
            if (world.GetBlock(cell).IsAir) {
                world.SetBlock(cell, block);
                placed = block;
            } else {
                world.SpawnDroppedItem(cell, new ItemStack(block)); // resting cell occupied — pop as an item
            }
            world.DestroyEntity(entity);
            world.Events?.PublishDeferred(new FallingBlockLanded(world, netId, cell, placed));
        }
    }
}
