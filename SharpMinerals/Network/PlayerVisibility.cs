using SharpMinerals.Entities.Components;
using SharpMinerals.Network.Messages;

namespace SharpMinerals.Network;

/// <summary>
/// Keeps every client's view of the other players in sync: profiles (tab list +
/// skins), entity spawns, movement, and removal. Sends are targeted per-client so a
/// player never receives its own spawn.
/// </summary>
public static class PlayerVisibility {
    const int CreativeMode = 1;

    static PlayerListEntry Entry(in NetworkedPlayer p) => new(p.Uuid, p.Name, CreativeMode, Listed: true, Latency: 0);

    /// <summary>A player just joined: introduce it to others and others to it.</summary>
    public static void OnJoin(Server server, NetClient client) {
        if (!server.TryGetPlayer(client.Id, out var self) || !self.World.Ecs.IsAlive(self.Entity))
            return;

        var selfInfo = self.World.Ecs.Get<NetworkedPlayer>(self.Entity);
        var selfPos = self.World.Ecs.Get<Transform>(self.Entity);

        // Everyone (including the newcomer, for its own tab list) learns the new
        // profile. Existing clients already have the newcomer's profile before we
        // spawn its entity to them below.
        server.NetServer.Broadcast(new PlayerInfoUpdateS2C(new[] { Entry(selfInfo) }),
            c => c.State == ConnectionState.Play);

        var others = new List<(ulong ClientId, NetworkedPlayer Info, Transform Pos)>();
        foreach (var (clientId, handle) in server.Players) {
            if (clientId == client.Id || !handle.World.Ecs.IsAlive(handle.Entity))
                continue;
            others.Add((clientId, handle.World.Ecs.Get<NetworkedPlayer>(handle.Entity),
                handle.World.Ecs.Get<Transform>(handle.Entity)));
        }

        if (others.Count > 0) {
            // The newcomer must learn the existing profiles BEFORE their entities are
            // spawned, or it can't render them.
            client.Send(new PlayerInfoUpdateS2C(others.Select(o => Entry(o.Info)).ToList()));
            foreach (var o in others)
                client.Send(Spawn(o.Info, o.Pos));
        }

        // Spawn the newcomer's entity for each existing client.
        foreach (var o in others)
            server.NetServer.Send(o.ClientId, Spawn(selfInfo, selfPos));
    }

    /// <summary>A player moved: push its new transform to everyone else.</summary>
    public static void OnMove(Server server, NetClient client) {
        if (!server.TryGetPlayer(client.Id, out var self) || !self.World.Ecs.IsAlive(self.Entity))
            return;

        var info = self.World.Ecs.Get<NetworkedPlayer>(self.Entity);
        var pos = self.World.Ecs.Get<Transform>(self.Entity);

        bool ToOthers(NetClient c) => c.State == ConnectionState.Play && c.Id != client.Id;
        server.NetServer.Broadcast(new TeleportEntityS2C(info.EntityId, pos.X, pos.Y, pos.Z, pos.Yaw, pos.Pitch, true), ToOthers);
        server.NetServer.Broadcast(new EntityHeadRotationS2C(info.EntityId, pos.Yaw), ToOthers);
    }

    /// <summary>A player left: despawn its entity and drop it from the tab list everywhere.</summary>
    public static void OnLeave(Server server, NetworkedPlayer info) {
        server.NetServer.Broadcast(new RemoveEntitiesS2C(new[] { info.EntityId }), c => c.State == ConnectionState.Play);
        server.NetServer.Broadcast(new PlayerInfoRemoveS2C(new[] { info.Uuid }), c => c.State == ConnectionState.Play);
    }

    static SpawnPlayerS2C Spawn(in NetworkedPlayer info, in Transform pos) =>
        new(info.EntityId, info.Uuid, pos.X, pos.Y, pos.Z, pos.Yaw, pos.Pitch);
}
