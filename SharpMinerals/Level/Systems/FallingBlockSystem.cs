using Arch.Core;
using SharpMinerals.Blocks;
using SharpMinerals.Blocks.Descriptors;
using SharpMinerals.Entities;
using SharpMinerals.Entities.Components;
using SharpMinerals.Math;
using SharpMinerals.Network;
using SharpMinerals.Network.Messages;
using ArchEntity = Arch.Core.Entity;

namespace SharpMinerals.Level.Systems;

/// <summary>Owns falling blocks (sand/gravel) end to end: detects when one rests on the ground and fires its
/// <see cref="IOnLand"/> reaction (re-place, pop as an item, or whatever the block type defines), and projects
/// the spawn + landing to clients via <see cref="INetworkSystem"/>. Because sim and its client view live in the
/// one class, the landing hand-off is just a field - no ECS tag or extra query.</summary>
public sealed class FallingBlockSystem : ITickable, INetworkSystem {
    static readonly QueryDescription LiveQuery =
        new QueryDescription().WithAll<FallingBlockEntityComponent, TransformEntityComponent>();
    static readonly QueryDescription GroundedQuery =
        new QueryDescription().WithAll<FallingBlockEntityComponent, TransformEntityComponent, BlockCollisionEntityComponent>();

    static readonly Vector3i Down = new(0, -1, 0);
    static readonly Vector3i Up = new(0, 1, 0);

    readonly World world;
    readonly List<(ArchEntity Entity, BlockType Block, Vector3i Cell)> grounded = new();
    readonly List<(int NetId, Vector3i Cell)> landed = new(); // Tick -> Flush hand-off (sequential phases, same instance)

    public FallingBlockSystem(World world) => this.world = world;

    /// <summary>If the block at <paramref name="pos"/> is gravity-bound with air beneath it, detaches it into a
    /// falling-block entity (clearing + broadcasting the cell) and walks up the column so a whole stack detaches
    /// at once. Called from placement/break, not the tick - hence static, taking the server + world directly.</summary>
    public static void TryStartFalling(Server server, World world, Vector3i pos) {
        var block = world.GetBlock(pos);
        if (!block.Has<FallingBlockDescriptor>()) return;
        if (!world.GetBlock(pos + Down).IsAir) return; // still supported - stays put

        world.SetBlock(pos, BlockRegistry.Air);
        server.BroadcastInRange(world, pos.X + 0.5, pos.Z + 0.5, new BlockUpdateS2C(pos, BlockRegistry.Air));
        world.SpawnFallingBlock(pos, block);

        TryStartFalling(server, world, pos + Up); // the block above lost its support too - propagate up
    }

    public void Tick() {
        var ecs = world.Ecs;
        grounded.Clear();
        ecs.Query(in GroundedQuery, (ArchEntity e, ref FallingBlockEntityComponent f, ref TransformEntityComponent t, ref BlockCollisionEntityComponent fb) => {
            if (f.EntityId == 0 || !fb.OnGround) return; // unannounced (client can't track it) or still falling
            grounded.Add((e, f.Block, ToCell(t)));
        });

        foreach (var (entity, block, cell) in grounded) {
            if (!ecs.IsAlive(entity)) continue;
            int netId = ecs.Get<FallingBlockEntityComponent>(entity).EntityId;
            var ctx = new BlockContext { World = world, Position = cell, Block = block };
            foreach (var b in block.GetAll<IOnLand>())
                b.OnLand(in ctx);
            world.DestroyEntity(entity);
            landed.Add((netId, cell));
        }
    }

    /// <summary>Pre-tick: give each freshly-detached block a network id and announce its spawn, so the client
    /// tracks it from its un-decayed position and mirrors the fall.</summary>
    public void Announce(Server server) {
        var ecs = world.Ecs;
        var fresh = new List<(ArchEntity Entity, BlockType Block, TransformEntityComponent Pos)>();
        ecs.Query(in LiveQuery, (ArchEntity e, ref FallingBlockEntityComponent f, ref TransformEntityComponent t) => {
            if (f.EntityId == 0) fresh.Add((e, f.Block, t));
        });

        foreach (var (entity, block, pos) in fresh) {
            int id = server.NextEntityId();
            ecs.Get<FallingBlockEntityComponent>(entity).EntityId = id;
            SendSpawn(m => server.NetServer.Broadcast(m, c => c.State == ConnectionState.Play), id, block, pos);
        }
    }

    /// <summary>Writes the spawn packet for a falling block (carrying its block as the spawn Data) to
    /// <paramref name="send"/> - a broadcast from <see cref="Announce"/>, or a targeted send when an existing
    /// falling block is shown to a joining player (<see cref="Network.EntityVisibility"/>).</summary>
    public static void SendSpawn(Action<IMessage> send, int id, BlockType block, TransformEntityComponent pos) =>
        send(new SpawnEntityS2C(
            EntityId: id, Uuid: Guid.NewGuid(), Type: EntityRegistry.FallingBlock,
            X: pos.X, Y: pos.Y, Z: pos.Z, Pitch: 0, Yaw: 0, HeadYaw: 0,
            Data: 0, VelocityX: 0, VelocityY: 0, VelocityZ: 0, BlockData: block));

    /// <summary>Post-tick: broadcast each landed cell's resulting block (whatever the reaction left there) and
    /// remove the now-despawned entity.</summary>
    public void Flush(Server server) {
        if (landed.Count == 0) return;
        var ids = new int[landed.Count];
        for (int i = 0; i < landed.Count; i++) {
            var (netId, cell) = landed[i];
            server.BroadcastInRange(world, cell.X + 0.5, cell.Z + 0.5,
                new BlockUpdateS2C(cell, world.GetBlock(cell), world.GetBlockState(cell)));
            ids[i] = netId;
        }
        server.NetServer.Broadcast(new RemoveEntitiesS2C(ids), c => c.State == ConnectionState.Play);
        landed.Clear();
    }

    static Vector3i ToCell(in TransformEntityComponent t) =>
        new((int)System.Math.Floor(t.X), (int)System.Math.Floor(t.Y), (int)System.Math.Floor(t.Z));
}
