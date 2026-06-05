using Arch.Core;
using SharpMinerals.Entities.Components;
using SharpMinerals.Network;
using SharpMinerals.Network.Messages;
using ArchEntity = Arch.Core.Entity;

namespace SharpMinerals.Level.Systems;

/// <summary>Relays each player's movement to the other clients. Each tick it diffs the live transform against
/// the last one broadcast (<see cref="NetTransformEntityComponent"/>); on a change it sends Teleport Entity
/// + head rotation to everyone else and records the new transform. Replaces the old PlayerMoved event - the
/// per-tick diff coalesces a burst of movement packets into one broadcast and costs nothing while idle.</summary>
public sealed class PlayerMovementSystem : ITickable, INetworkSystem {
    static readonly QueryDescription PlayerQuery =
        new QueryDescription().WithAll<NetPlayerEntityComponent, TransformEntityComponent, NetTransformEntityComponent>();

    readonly World world;
    public PlayerMovementSystem(World world) => this.world = world;

    public void Tick() { } // relay only, in Flush

    public void Flush(Server server) {
        world.Ecs.Query(in PlayerQuery, (ArchEntity e, ref NetPlayerEntityComponent net, ref TransformEntityComponent t, ref NetTransformEntityComponent s) => {
            if (t.Position == s.Position && t.Yaw == s.Yaw && t.Pitch == s.Pitch)
                return; // unchanged since the last broadcast

            ulong selfId = net.ClientId;
            int eid = net.EntityId;
            bool ToOthers(NetClient c) => c.InWorld && c.Id != selfId; // in-world recipients, never the mover

            server.NetServer.Broadcast(new TeleportEntityS2C(eid, t.X, t.Y, t.Z, t.Yaw, t.Pitch, true), ToOthers);
            server.NetServer.Broadcast(new EntityHeadRotationS2C(eid, t.Yaw), ToOthers);

            s.Position = t.Position; s.Yaw = t.Yaw; s.Pitch = t.Pitch;
        });
    }
}
