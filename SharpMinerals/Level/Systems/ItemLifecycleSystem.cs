using Arch.Core;
using SharpMinerals.Entities;
using SharpMinerals.Entities.Components;
using SharpMinerals.Entities.Descriptors;
using SharpMinerals.Items;
using SharpMinerals.Math;
using SharpMinerals.Network;
using SharpMinerals.Network.Messages;
using ArchEntity = Arch.Core.Entity;

namespace SharpMinerals.Level.Systems;

/// <summary>Owns dropped items: ages each and ticks down its pickup delay (the pickup itself is in
/// <see cref="ItemPickupSystem"/>, which runs later once the delay elapsed), and announces freshly-spawned
/// drops to clients.</summary>
public sealed class ItemLifecycleSystem : ITickable, INetworkSystem {
    static readonly QueryDescription ItemLifecycleQuery = new QueryDescription().WithAll<PickupEntityComponent>();
    static readonly QueryDescription DropSpawnQuery =
        new QueryDescription().WithAll<PickupEntityComponent, TransformEntityComponent, VelocityEntityComponent>();

    // Wire velocity is 1/8000 of a block per tick; the client lerps from it and predicts the motion.
    const Mfloat VelocityUnit = 8000.0;
    static short ToWire(Mfloat blocksPerTick) =>
        (short)System.Math.Clamp(blocksPerTick * VelocityUnit, short.MinValue, short.MaxValue);

    readonly World world;

    public ItemLifecycleSystem(World world) => this.world = world;

    public void Tick() {
        world.Ecs.Query(in ItemLifecycleQuery, (ref PickupEntityComponent drop) => {
            drop.Age++;
            if (drop.PickupDelay > 0) drop.PickupDelay--;
        });
    }

    /// <summary>Pre-tick: give each freshly-spawned drop (id 0) a network id and announce its spawn.</summary>
    public void Announce(Server server) {
        var ecs = world.Ecs;
        var fresh = new List<(ArchEntity Entity, ItemStack Stack, TransformEntityComponent Pos, VelocityEntityComponent Vel)>();
        ecs.Query(in DropSpawnQuery, (ArchEntity e, ref PickupEntityComponent d, ref TransformEntityComponent t, ref VelocityEntityComponent v) => {
            if (d.EntityId == 0 && !d.Stack.IsEmpty) fresh.Add((e, d.Stack, t, v));
        });

        foreach (var (entity, stack, pos, vel) in fresh) {
            int id = server.NextEntityId();
            ecs.Get<PickupEntityComponent>(entity).EntityId = id;
            var kind = ecs.Get<TypeEntityDescriptor>(entity).Type;
            SendSpawn(m => Broadcast(server, m), id, kind, stack, pos, vel);
        }
    }

    /// <summary>Writes the packet sequence that makes a client render a dropped item (spawn + velocity + the item
    /// stack, bundled so it never shows a contents-less item) to <paramref name="send"/> - a broadcast from
    /// <see cref="Announce"/>, or a targeted send when an existing drop is shown to a joining player
    /// (<see cref="Network.EntityVisibility"/>). Velocity is a separate packet because the 1.20.1 client ignores
    /// the spawn-packet velocity for items.</summary>
    public static void SendSpawn(Action<IMessage> send, int id, EntityType kind, ItemStack stack,
                                 TransformEntityComponent pos, VelocityEntityComponent vel) {
        send(new BundleDelimiterS2C());
        send(new SpawnEntityS2C(
            EntityId: id, Uuid: Guid.NewGuid(), Type: kind,
            X: pos.X, Y: pos.Y, Z: pos.Z, Pitch: 0, Yaw: 0, HeadYaw: 0, Data: 0,
            VelocityX: 0, VelocityY: 0, VelocityZ: 0));
        send(new SetEntityVelocityS2C(id, ToWire(vel.X), ToWire(vel.Y), ToWire(vel.Z)));
        send(new SetItemEntityMetadataS2C(id, stack));
        send(new BundleDelimiterS2C());
    }

    static void Broadcast(Server server, IMessage message) =>
        server.NetServer.Broadcast(message, c => c.State == ConnectionState.Play);
}
