using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using SharpMinerals.Entities;
using SharpMinerals.Entities.Components;
using SharpMinerals.Level;
using SharpMinerals.Math;
using SharpMinerals.Network.Containers;
using SharpMinerals.Network.Buffers;
using SharpMinerals.Network.Messages;

namespace SharpMinerals.Network.Handlers;

/// <summary>
/// Turns decoded serverbound messages into server actions. This is the seam where
/// the wire protocol meets game logic: it drives the handshake/status flow, the
/// offline-mode login, and hands Play-state packets to <see cref="PlayPacketHandler"/>.
/// </summary>
public static class ServerPacketHandler {
    const byte CreativeMode = 1;

    static readonly ILogger Log = Logging.For("Play");

    public static void Handle(NetClient client, IMessage message) {
        switch (message) {
            case HandshakeC2S handshake:
                client.State = handshake.NextState switch {
                    1 => ConnectionState.Status,
                    2 => ConnectionState.Login,
                    var s => throw new FormatException($"Unknown next state {s} in handshake."),
                };
                // Pick the protocol this connection speaks from the version it announced.
                if (Server.Instance?.NetServer.Registry is { } registry) {
                    client.Protocol = registry.ForOrDefault(handshake.ProtocolVersion);
                    Log.LogDebug("#{Client} handshake: protocol {Version} -> {Name}",
                        client.Id, handshake.ProtocolVersion, client.Protocol.VersionName);
                }
                break;

            case StatusRequestC2S:
                client.Send(new StatusResponseS2C(BuildStatusJson(client)));
                break;

            case PingRequestC2S ping:
                // Echo the payload, then the client closes the connection.
                client.Send(new PongResponseS2C(ping.Payload));
                client.Disconnect();
                break;

            case LoginStartC2S start:
                HandleLogin(client, start);
                break;

            default:
                // Everything else is a Play-state packet.
                PlayPacketHandler.Handle(client, message);
                break;
        }
    }

    /// <summary>
    /// Offline-mode login: no encryption or authentication. We accept the name,
    /// derive a deterministic offline UUID, switch to Play, and send the opening
    /// play packets before spawning the player into the world.
    /// </summary>
    static void HandleLogin(NetClient client, LoginStartC2S start) {
        var server = Server.Instance;
        if (server is null) return;

        // Offline mode derives the UUID from the name, so two clients on the same
        // account would collide — disambiguate so they're distinct players.
        string name = Disambiguate(server, start.Name);
        var uuid = OfflineUuid(name);
        Log.LogInformation("Login (offline) for {Name} → {Uuid}", name, uuid);

        client.Send(new LoginSuccessS2C(uuid, name));
        client.State = ConnectionState.Play;

        int entityId = server.AddPlayer(client, name, uuid);

        client.Send(new JoinGameS2C(
            EntityId: entityId,
            GameMode: CreativeMode,
            DimensionName: "minecraft:overworld",
            HashedSeed: 0,
            ViewDistance: 10,
            ReducedDebugInfo: false));

        // Mark the spawn chunk as the centre BEFORE streaming chunks, or the client
        // discards them and never leaves the loading screen.
        client.Send(new SetCenterChunkS2C(0, 0));
        client.Send(new SetDefaultSpawnPositionS2C(new Vector3i(0, FlatChunkGenerator.SurfaceY, 0), 0f));

        // Stream the chunks around spawn so the client has terrain to stand on,
        // then place the player. The position sync must come after the chunks.
        SendSpawnChunks(server.DefaultWorld, client, chunkRadius: 3);
        client.Send(new SynchronizePlayerPositionS2C(
            0.5, FlatChunkGenerator.SurfaceY, 0.5, 0f, 0f, TeleportId: 1));
        client.Send(new SetHealthS2C(Player.MaxHealth, 20, 5f));

        // Send the player's inventory (window 0) so the client reflects the server's contents.
        if (server.TryGetPlayer(client.Id, out var handle) && handle.World.Ecs.IsAlive(handle.Entity)) {
            var inv = handle.World.Ecs.Get<EntityInventory>(handle.Entity);
            client.Send(new SetContainerContentS2C(0, 0, ContainerManager.PlayerWindow(inv), default));
            client.Send(new SetHeldItemS2C(inv.SelectedSlot));
        }

        // Introduce this player to everyone else (and vice versa).
        PlayerVisibility.OnJoin(server, client);
    }

    static string Disambiguate(Server server, string name) {
        bool Taken(string n) => server.Players.Any(kv =>
            kv.Value.World.Ecs.IsAlive(kv.Value.Entity) &&
            kv.Value.World.Ecs.Get<NetworkedPlayer>(kv.Value.Entity).Name == n);

        if (!Taken(name)) return name;
        for (int i = 2; ; i++) {
            var candidate = $"{name}_{i}";
            if (!Taken(candidate)) return candidate;
        }
    }

    static void SendSpawnChunks(World world, NetClient client, int chunkRadius) {
        int sent = 0;
        for (int cx = -chunkRadius; cx <= chunkRadius; cx++)
            for (int cz = -chunkRadius; cz <= chunkRadius; cz++) {
                client.Send(ChunkSerializer.Build(world, cx, cz));
                sent++;
            }
        Log.LogInformation("Sent {Count} chunk columns (radius {Radius}) to #{Client}", sent, chunkRadius, client.Id);
    }

    /// <summary>
    /// Java's offline-mode UUID: a version-3 (MD5) UUID over "OfflinePlayer:&lt;name&gt;".
    /// </summary>
    public static Guid OfflineUuid(string name) {
        Span<byte> hash = stackalloc byte[16];
        MD5.HashData(Encoding.UTF8.GetBytes("OfflinePlayer:" + name), hash);
        hash[6] = (byte)((hash[6] & 0x0F) | 0x30); // version 3
        hash[8] = (byte)((hash[8] & 0x3F) | 0x80); // IETF variant
        return MinecraftStream.GuidFromBigEndianBytes(hash);
    }

    /// <summary>Builds the server-list-ping JSON from the live server state.</summary>
    static string BuildStatusJson(NetClient client) {
        var server = Server.Instance!;
        var protocol = client.Protocol; // echo the connecting client's version so it shows compatible

        var status = new {
            version = new { name = protocol.VersionName, protocol = protocol.Version },
            players = new { max = server.MaxPlayers, online = server.PlayerCount, sample = Array.Empty<object>() },
            description = new { text = server.MOTD },
        };

        return JsonSerializer.Serialize(status);
    }
}
