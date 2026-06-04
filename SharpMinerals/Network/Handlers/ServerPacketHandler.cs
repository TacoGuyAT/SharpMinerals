using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using SharpMinerals.Commands;
using SharpMinerals.Entities.Components;
using SharpMinerals.Events;
using SharpMinerals.Level;
using SharpMinerals.Math;
using SharpMinerals.Network.Containers;
using SharpMinerals.Network.Buffers;
using SharpMinerals.Network.Messages;
using SharpMinerals.Network.Protocols.JE61;

namespace SharpMinerals.Network.Handlers;

/// <summary>
/// Turns decoded serverbound messages into server actions: drives the handshake/status flow, the
/// offline-mode login, and hands Play-state packets to <see cref="PlayPacketHandler"/>.
/// </summary>
public sealed class ServerPacketHandler {
    const byte CreativeMode = 1;

    readonly ILogger Log = Logging.For<ServerPacketHandler>();

    readonly Server server;
    readonly PlayPacketHandler play;

    public ServerPacketHandler(Server server) {
        this.server = server;
        play = new PlayPacketHandler(server);
    }

    public void Handle(NetClient client, IMessage message) {
        switch (message) {
            case HandshakeC2S handshake:
                client.State = handshake.NextState switch {
                    1 => ConnectionState.Status,
                    2 => ConnectionState.Login,
                    var s => throw new FormatException($"Unknown next state {s} in handshake."),
                };
                // Pick the protocol this connection speaks from the version it announced.
                client.Protocol = server.NetServer.Registry.ForOrDefault(handshake.ProtocolVersion);
                Log.LogDebug("#{Client} handshake: protocol {Version} -> {Name}",
                    client.Id, handshake.ProtocolVersion, client.Protocol.VersionName);
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

            case LegacyServerListPingC2S:
                HandleLegacyPing(client);
                break;

            // Legacy (1.5.2) login handshake: 0x02 -> 0xFD -> 0xFC -> enable AES -> 0xCD -> 0x01.
            case LegacyHandshakeC2S hs:
                HandleLegacyHandshake(client, hs);
                break;

            case LegacyEncryptionResponseC2S enc:
                HandleLegacyEncryptionResponse(client, enc);
                break;

            case LegacyClientStatusesC2S:
                HandleLegacyClientStatuses(client);
                break;

            default:
                // Everything else is a Play-state packet. Unmatched legacy in-world packets are harmlessly
                // ignored (decoded only to keep the length-prefix-free legacy stream in sync).
                play.Handle(client, message);
                break;
        }
    }

    /// <summary>Offline-mode login (no encryption or auth).</summary>
    void HandleLogin(NetClient client, LoginStartC2S start) => EnterWorld(client, start.Name);

    /// <summary>Brings a logged-in client into the world synchronously on the receive thread, so login
    /// packets and the chunk stream go out in order. ECS entity creation is serialized against the tick.</summary>
    void EnterWorld(NetClient client, string requestedName) {
        // Offline UUIDs derive from the name, so same-account clients would collide; disambiguate.
        string name = Disambiguate(requestedName);
        client.PlayerName = name;
        var uuid = OfflineUuid(name);
        Log.LogInformation("Login (offline) for {Name} -> {Uuid}", name, uuid);

        client.Send(new LoginSuccessS2C(uuid, name));
        client.State = ConnectionState.Play;

        int entityId = server.AddPlayer(client, name, uuid);

        // Fetch the world up front so Join Game advertises the same dimension key a later world switch's Respawn uses.
        if (!server.TryGetPlayer(client.Id, out var context))
            return; // just added above, so this never trips

        client.Send(new JoinGameS2C(
            EntityId: entityId,
            GameMode: CreativeMode,
            DimensionName: context.World.Name,
            HashedSeed: 0,
            ViewDistance: 10,
            ReducedDebugInfo: false));

        // Advertise the server brand so the client's F3 screen shows us instead of "null".
        client.Send(new BrandS2C($"SharpMinerals v{server.Version}"));

        // Read the player's placement (possibly restored) to sync the client; terrain + visibility via PlayerJoined.
        var ecs = context.World.Ecs;
        var spawn = ecs.Get<TransformEntityComponent>(context.Entity);
        var health = ecs.Get<HealthEntityComponent>(context.Entity);
        var inventory = ecs.Get<InventoryEntityComponent>(context.Entity);

        // Advertise the command tree (filtered to what this player may run) for tab-completion.
        client.Send(new DeclareCommandsS2C(new SenderContext(ecs.Get<SenderEntityComponent>(context.Entity), server.CommandDispatcher, client)));

        client.Send(new SetDefaultSpawnPositionS2C(new Vector3i(0, WorldDefaults.SurfaceY, 0), 0f));

        // Send the player's own chunk column first so the client can place it on loaded terrain immediately,
        // instead of free-falling through empty space until terrain arrives. The position sync rides the same
        // (bulk) send lane right behind that column, and the surrounding columns stream afterward.
        Streaming.StreamSpawnColumn(context);

        client.Send(new SynchronizePlayerPositionS2C(
            spawn.X, spawn.Y, spawn.Z, spawn.Yaw, spawn.Pitch, TeleportId: server.BeginTeleport(client.Id)));
        client.Send(new SetHealthS2C(health.Current, 20, 5f));

        // Stream the rest of the view (skips the spawn column) and spawn the player to other players.
        server.Events.Publish(new PlayerJoined(context));

        client.Send(new SetContainerContentS2C(0, 0, ContainerManager.PlayerWindow(inventory), default));
        client.Send(new SetHeldItemS2C(inventory.SelectedSlot));
    }

    /// <summary>
    /// Legacy (1.5.2) server-list ping: reply with a 0xFF kick whose text is the §1-delimited status string.
    /// The legacy protocol has no JSON or Status state; the ping response IS a kick.
    /// </summary>
    void HandleLegacyPing(NetClient client) {
        var p = client.Protocol;
        // §1\0<protocol>\0<version>\0<motd>\0<online>\0<max>
        string status = string.Join('\0',
            "§1", p.Version.ToString(), p.VersionName, server.MOTD,
            server.PlayerCount.ToString(), server.MaxPlayers.ToString());
        Log.LogInformation("#{Client} legacy ({Version}) server-list ping", client.Id, p.VersionName);
        client.Send(new LegacyKickS2C(status));
        // Do NOT close: 1.5.2 clients race - closing right after 0xFF resets the connection before they read it.
        // Let the client close on receipt; the receive loop ends on EOF.
    }

    /// <summary>
    /// Legacy login step 1 (client 0x02): store the name and request encryption (0xFD) with the RSA public key.
    /// Offline mode uses server id "-" (client skips auth), but AES encryption is still mandatory.
    /// </summary>
    void HandleLegacyHandshake(NetClient client, LegacyHandshakeC2S hs) {
        if (client.Protocol is not ProtocolJE61 je61) return;
        client.PlayerName = hs.Username;
        Log.LogInformation("Legacy (1.5.2) login start: {Name} (protocol {Proto})", hs.Username, hs.ProtocolVersion);
        // Verify token is required but unvalidated in offline mode, so a random one suffices.
        client.Send(new LegacyEncryptionRequestS2C("-", je61.PublicKeyDer, RandomNumberGenerator.GetBytes(4)));
    }

    /// <summary>
    /// Legacy login step 2 (client 0xFC): RSA-decrypt the shared secret, send the empty 0xFC, switch to AES/CFB8.
    /// </summary>
    static void HandleLegacyEncryptionResponse(NetClient client, LegacyEncryptionResponseC2S enc) {
        if (client.Protocol is not ProtocolJE61 je61) return;

        // Offline mode doesn't validate the verify token (a wrong key just yields AES garbage and drops next packet).
        byte[] sharedSecret = je61.DecryptRsa(enc.SharedSecret);
        // Empty 0xFC goes out PLAINTEXT, then encryption turns on, so subsequent packets are AES/CFB8.
        client.Send(new LegacyEncryptionAcceptS2C());
        client.EnableEncryption(sharedSecret);
    }

    /// <summary>Legacy login step 3 (client 0xCD "initial spawn", encrypted): brings the player into the world.</summary>
    void HandleLegacyClientStatuses(NetClient client) => EnterLegacyWorld(client);

    void EnterLegacyWorld(NetClient client) {
        string name = Disambiguate(client.PlayerName ?? "Player");
        client.PlayerName = name;

        // Spawn a real ECS player (position/rotation/health/inventory, restored if they've played before).
        int entityId = server.AddPlayer(client, name, OfflineUuid(name));
        if (!server.TryGetPlayer(client.Id, out var context)) return;
        var t = context.World.Ecs.Get<TransformEntityComponent>(context.Entity);

        client.Send(new LegacyLoginRequestS2C(
            entityId, LevelType: "flat", GameMode: CreativeMode, Dimension: 0, Difficulty: 1,
            MaxPlayers: (byte)System.Math.Min(server.MaxPlayers, 255)));
        client.Send(new LegacySpawnPositionS2C((int)t.X, (int)t.Y, (int)t.Z));
        // Same lifecycle event as a modern join. Modern players see this legacy player; the modern packets
        // aimed back at it are dropped by CanEncode (legacy clientbound entity packets are future work).
        // Play state only affects the protocol-agnostic InWorld checks (legacy decoding uses a fixed state).
        client.State = ConnectionState.Play;
        server.Events.Publish(new PlayerJoined(context));
        client.Send(new LegacyPlayerPositionLookS2C(t.X, t.Y, t.Z, t.Yaw, t.Pitch, true));

        Log.LogInformation("Legacy player in world: {Name} (eid {Eid})", name, entityId);
    }

    string Disambiguate(string name) {
        bool Taken(string n) => server.Players.Any(kv =>
            kv.Value.World.Ecs.IsAlive(kv.Value.Entity) &&
            kv.Value.World.Ecs.Get<NetPlayerEntityComponent>(kv.Value.Entity).Name == n);

        if (!Taken(name)) return name;
        for (int i = 2; ; i++) {
            var candidate = $"{name}_{i}";
            if (!Taken(candidate)) return candidate;
        }
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
    string BuildStatusJson(NetClient client) {
        var protocol = client.Protocol; // echo the connecting client's version so it shows compatible

        var status = new StatusResponse(
            new StatusVersion(protocol.VersionName, protocol.Version),
            new StatusPlayers(server.MaxPlayers, server.PlayerCount, Array.Empty<StatusSample>()),
            new StatusDescription(server.MOTD));

        return JsonSerializer.Serialize(status, StatusJsonContext.Default.StatusResponse);
    }
}
