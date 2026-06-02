using Arch.Core;
using SharpMinerals.Blocks;
using SharpMinerals.Blocks.Descriptors;
using SharpMinerals.Entities;
using SharpMinerals.Entities.Components;
using SharpMinerals.Entities.Descriptors;
using SharpMinerals.Events;
using SharpMinerals.Items;
using SharpMinerals.Math;
using SharpMinerals.Network.Containers;
using SharpMinerals.Network.Messages;
using World = SharpMinerals.Level.World;

namespace SharpMinerals.Network.Handlers;

/// <summary>
/// The networking edge for simulated entities (dropped items, falling blocks); the simulation itself lives
/// in world systems. Announces spawns, triggers block falls, and broadcasts deferred pickup/landing effects.
/// </summary>
public static class EntityNetworking {
    static readonly QueryDescription DropQuery =
        new QueryDescription().WithAll<PickupEntityComponent, TransformEntityComponent, VelocityEntityComponent>();
    static readonly QueryDescription FallingQuery =
        new QueryDescription().WithAll<FallingBlockEntityComponent, TransformEntityComponent>();

    static readonly Vector3i Down = new(0, -1, 0);
    static readonly Vector3i Up = new(0, 1, 0);

    // Wire velocity is 1/8000 of a block per tick; the client lerps from it and predicts the motion.
    const Mfloat VelocityUnit = 8000.0;
    static short ToWire(Mfloat blocksPerTick) =>
        (short)System.Math.Clamp(blocksPerTick * VelocityUnit, short.MinValue, short.MaxValue);

    /// <summary>Called once at startup.</summary>
    public static void RegisterHandlers(Server server) {
        server.Events.Subscribe<ItemPickedUp>(e => OnItemPickedUp(server, e));
        server.Events.Subscribe<FallingBlockLanded>(e => OnFallingBlockLanded(server, e));
    }

    /// <summary>Announces newly-spawned drops and falling blocks (network id 0). Call BEFORE the physics
    /// tick so the client gets each entity's un-decayed spawn position + velocity and mirrors its motion.</summary>
    public static void AnnounceNew(Server server) {
        foreach (var world in server.Worlds.Values) {
            AnnounceDrops(server, world);
            AnnounceFallingBlocks(server, world);
        }
    }

    static void AnnounceDrops(Server server, World world) {
        var pending = new List<(Entity Entity, ItemStack Stack, TransformEntityComponent Pos, VelocityEntityComponent Vel)>();
        world.Ecs.Query(in DropQuery, (Entity e, ref PickupEntityComponent d, ref TransformEntityComponent t, ref VelocityEntityComponent v) => {
            if (d.EntityId == 0 && !d.Stack.IsEmpty) pending.Add((e, d.Stack, t, v));
        });

        foreach (var (entity, stack, pos, vel) in pending) {
            int id = server.NextEntityId();
            world.Ecs.Get<PickupEntityComponent>(entity).EntityId = id;
            var kind = world.Ecs.Get<TypeEntityDescriptor>(entity).Type;
            short vx = ToWire(vel.X), vy = ToWire(vel.Y), vz = ToWire(vel.Z);
            // Bundle the spawn + its item data so the client never sees a contents-less item.
            Broadcast(server, new BundleDelimiterS2C());
            Broadcast(server, new SpawnEntityS2C(
                EntityId: id, Uuid: Guid.NewGuid(), Type: kind,
                X: pos.X, Y: pos.Y, Z: pos.Z, Pitch: 0, Yaw: 0, HeadYaw: 0, Data: 0,
                VelocityX: 0, VelocityY: 0, VelocityZ: 0));
            // The 1.20.1 client doesn't reliably apply the Spawn Entity velocity, so set it explicitly.
            Broadcast(server, new SetEntityVelocityS2C(id, vx, vy, vz));
            Broadcast(server, new SetItemEntityMetadataS2C(id, stack));
            Broadcast(server, new BundleDelimiterS2C());
        }
    }

    static void AnnounceFallingBlocks(Server server, World world) {
        var pending = new List<(Entity Entity, BlockType Block, TransformEntityComponent Pos)>();
        world.Ecs.Query(in FallingQuery, (Entity e, ref FallingBlockEntityComponent f, ref TransformEntityComponent t) => {
            if (f.EntityId == 0) pending.Add((e, f.Block, t));
        });

        foreach (var (entity, block, pos) in pending) {
            int id = server.NextEntityId();
            world.Ecs.Get<FallingBlockEntityComponent>(entity).EntityId = id;
            Broadcast(server, new SpawnEntityS2C(
                EntityId: id, Uuid: Guid.NewGuid(), Type: EntityRegistry.FallingBlock,
                X: pos.X, Y: pos.Y, Z: pos.Z, Pitch: 0, Yaw: 0, HeadYaw: 0,
                Data: 0, VelocityX: 0, VelocityY: 0, VelocityZ: 0, BlockData: block));
        }
    }

    /// <summary>
    /// If the block at <paramref name="pos"/> is gravity-bound and has air beneath it, detaches it into a
    /// falling-block entity (clearing the cell and broadcasting that change), then walks up the column so a
    /// stack of sand/gravel all detaches at once. Called after a block is placed or its support is removed.
    /// </summary>
    public static void TryStartFalling(Server server, World world, Vector3i pos) {
        var block = world.GetBlock(pos);
        if (!block.Has<FallingBlockDescriptor>()) return;
        if (!world.GetBlock(pos + Down).IsAir) return; // still supported — stays put

        world.SetBlock(pos, BlockRegistry.Air);
        BroadcastBlockChange(server, new BlockUpdateS2C(pos, BlockRegistry.Air));
        world.SpawnFallingBlock(pos, block);

        // The block now above the cleared cell has lost its support too — propagate up the column.
        TryStartFalling(server, world, pos + Up);
    }

    // ── Deferred-event subscribers (run on the tick thread when the bus drains) ──────────────────────
    static void OnItemPickedUp(Server server, ItemPickedUp e) {
        var ecs = e.World.Ecs;
        if (!ecs.IsAlive(e.Collector)) return;
        int collectorNetId = ecs.Get<NetPlayerEntityComponent>(e.Collector).EntityId;

        // Pickup animation (the item flies into the collector), then remove or shrink the ground stack.
        Broadcast(server, new CollectItemS2C(e.PickupNetId, collectorNetId, e.Count));
        if (e.Leftover.IsEmpty)
            Broadcast(server, new RemoveEntitiesS2C(new[] { e.PickupNetId }));
        else
            Broadcast(server, new SetItemEntityMetadataS2C(e.PickupNetId, e.Leftover));

        // Resync the collector's window and refresh the equipment others see (a picked item may be held).
        if (ecs.Get<SenderEntityComponent>(e.Collector).Client is { } client) {
            var inventory = ecs.Get<InventoryEntityComponent>(e.Collector);
            server.NetServer.Send(client.Id, new SetContainerContentS2C(0, 0, ContainerManager.PlayerWindow(inventory), default));
            if (server.TryGetPlayer(client.Id, out var ctx))
                server.Events.Publish(new PlayerInventoryChanged(ctx));
        }
    }

    static void OnFallingBlockLanded(Server server, FallingBlockLanded e) {
        if (e.PlacedBlock is { } block)
            BroadcastBlockChange(server, new BlockUpdateS2C(e.Cell, block));
        Broadcast(server, new RemoveEntitiesS2C(new[] { e.NetId }));
    }

    static void Broadcast(Server server, IMessage message) =>
        server.NetServer.Broadcast(message, c => c.State == ConnectionState.Play);

    static void BroadcastBlockChange(Server server, BlockUpdateS2C message) =>
        server.NetServer.Broadcast(message, c => c.InWorld);
}
