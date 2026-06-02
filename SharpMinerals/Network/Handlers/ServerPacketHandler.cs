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
/// Turns decoded serverbound messages into server actions. This is the seam where
/// the wire protocol meets game logic: it drives the handshake/status flow, the
/// offline-mode login, and hands Play-state packets to <see cref="PlayPacketHandler"/>.
/// <para/>
/// An instance bound to its <see cref="Server"/> (injected once) — the transport calls
/// <see cref="Handle"/> per decoded message. Holds the server in a field rather than threading it
/// through every method.
/// </summary>
public sealed class ServerPacketHandler {
    const byte CreativeMode = 1;

    readonly ILogger Log = Logging.For("Play");

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

            // Legacy (1.5.2) login handshake: 0x02 → 0xFD → 0xFC → enable AES → 0xCD → 0x01.
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
                // Everything else is a Play-state packet. Legacy in-world packets (movement, settings,
                // keep-alive echoes, …) have no matching case here and are harmlessly ignored — they're
                // decoded only so the length-prefix-free legacy stream stays in sync.
                play.Handle(client, message);
                break;
        }
    }

    /// <summary>
    /// Offline-mode login: no encryption or authentication. We accept the name,
    /// derive a deterministic offline UUID, switch to Play, and send the opening
    /// play packets before spawning the player into the world.
    /// </summary>
    void HandleLogin(NetClient client, LoginStartC2S start) => EnterWorld(client, start.Name);

    /// <summary>Brings a logged-in client into the world synchronously (on the receive thread, so the
    /// login packets and chunk stream go out in order while the client waits). The one structural step —
    /// the ECS entity creation in <see cref="Server.AddPlayer"/> — is serialized against the tick there.</summary>
    void EnterWorld(NetClient client, string requestedName) {
        // Offline mode derives the UUID from the name, so two clients on the same
        // account would collide — disambiguate so they're distinct players.
        string name = Disambiguate(requestedName);
        client.PlayerName = name;
        var uuid = OfflineUuid(name);
        Log.LogInformation("Login (offline) for {Name} → {Uuid}", name, uuid);

        client.Send(new LoginSuccessS2C(uuid, name));
        client.State = ConnectionState.Play;

        int entityId = server.AddPlayer(client, name, uuid);

        // Fetch the player's world up front so Join Game advertises the SAME dimension key a later world
        // switch will Respawn with — keeping the client's world key consistent from the first packet.
        if (!server.TryGetPlayer(client.Id, out var context))
            return; // just added above, so this never trips — keeps the (now class) context non-null

        client.Send(new JoinGameS2C(
            EntityId: entityId,
            GameMode: CreativeMode,
            DimensionName: context.World.Name,
            HashedSeed: 0,
            ViewDistance: 10,
            ReducedDebugInfo: false));

        // Advertise the server brand ("server vendor") so the client's F3 screen
        // shows us instead of "null".
        client.Send(new BrandS2C($"SharpMinerals v{server.Version}"));

        // Read the player's actual placement (it may have been restored from the store) so we
        // can sync the client to it; terrain + visibility are handled by PlayerJoined subscribers.
        var ecs = context.World.Ecs;
        var spawn = ecs.Get<TransformEntityComponent>(context.Entity);
        var health = ecs.Get<HealthEntityComponent>(context.Entity);
        var inventory = ecs.Get<InventoryEntityComponent>(context.Entity);

        // Advertise the command tree so the client can tab-complete (and ask us for server-side suggestions on
        // ask_server arguments). Filtered to what this player may run.
        client.Send(new DeclareCommandsS2C(new SenderContext(ecs.Get<SenderEntityComponent>(context.Entity), server.CommandDispatcher, client)));

        client.Send(new SetDefaultSpawnPositionS2C(new Vector3i(0, FlatChunkGenerator.SurfaceY, 0), 0f));

        // Player joined: subscribers stream the initial chunk view (centre chunk + the surrounding
        // columns) and introduce this player to the others. This must run BEFORE the position sync —
        // the client discards chunks sent without a centre and needs terrain before being placed.
        server.Events.Publish(new PlayerJoined(context));

        client.Send(new SynchronizePlayerPositionS2C(
            spawn.X, spawn.Y, spawn.Z, spawn.Yaw, spawn.Pitch, TeleportId: server.BeginTeleport(client.Id)));
        client.Send(new SetHealthS2C(health.Current, 20, 5f));

        // Send the player's inventory (window 0) so the client reflects the server's contents.
        client.Send(new SetContainerContentS2C(0, 0, ContainerManager.PlayerWindow(inventory), default));
        client.Send(new SetHeldItemS2C(inventory.SelectedSlot));
    }

    /// <summary>
    /// Legacy (1.5.2 / protocol 61) server-list ping: reply with a 0xFF kick whose text is the
    /// §1-delimited status string (protocol, version, MOTD, online/max players), then close. There is
    /// no JSON and no Status state in the legacy protocol — the ping response IS a kick.
    /// </summary>
    void HandleLegacyPing(NetClient client) {
        var p = client.Protocol;
        // §1\0<protocol>\0<version>\0<motd>\0<online>\0<max>
        string status = string.Join('\0',
            "§1", p.Version.ToString(), p.VersionName, server.MOTD,
            server.PlayerCount.ToString(), server.MaxPlayers.ToString());
        Log.LogInformation("#{Client} legacy ({Version}) server-list ping", client.Id, p.VersionName);
        client.Send(new LegacyKickS2C(status));
        // Do NOT close the socket here. 1.5.2 clients have a documented race: closing immediately after
        // the 0xFF response resets the connection before they read it, so the server list shows nothing.
        // Leave it open and let the client close on receipt — the receive loop then ends on EOF.
    }

    /// <summary>
    /// Legacy login step 1: the client's handshake (0x02). Store the name, mint a 4-byte verify token,
    /// and request encryption (0xFD) with the server's RSA public key. Offline mode uses server id "-"
    /// (the client then skips session.minecraft.net auth) — but the AES encryption itself is mandatory.
    /// </summary>
    void HandleLegacyHandshake(NetClient client, LegacyHandshakeC2S hs) {
        if (client.Protocol is not ProtocolJE61 je61) return;
        client.PlayerName = hs.Username;
        Log.LogInformation("Legacy (1.5.2) login start: {Name} (protocol {Proto})", hs.Username, hs.ProtocolVersion);
        // 0xFD requires a verify token; offline mode doesn't validate the echo, so a random one suffices.
        client.Send(new LegacyEncryptionRequestS2C("-", je61.PublicKeyDer, RandomNumberGenerator.GetBytes(4)));
    }

    /// <summary>
    /// Legacy login step 2: the client's 0xFC. RSA-decrypt the shared secret + verify token, check the
    /// token, then (offline: no Mojang auth) send the empty 0xFC and switch the connection to AES/CFB8.
    /// </summary>
    static void HandleLegacyEncryptionResponse(NetClient client, LegacyEncryptionResponseC2S enc) {
        if (client.Protocol is not ProtocolJE61 je61) return;

        // RSA-decrypt the shared secret (needed for AES). Offline mode does NOT authenticate or validate
        // the echoed verify token — a wrong key just yields AES garbage and the connection drops next
        // packet — so there's no per-connection login state to keep.
        byte[] sharedSecret = je61.DecryptRsa(enc.SharedSecret);
        // Empty 0xFC goes out PLAINTEXT, then encryption turns on — so our next packet (and the client's)
        // are AES/CFB8.
        client.Send(new LegacyEncryptionAcceptS2C());
        client.EnableEncryption(sharedSecret);
    }

    /// <summary>
    /// Legacy login step 3: the client's 0xCD ("initial spawn", over the now-encrypted channel). Send the
    /// Login Request (0x01). World streaming (spawn position, the 1.5.2 chunk format, keep-alives) is
    /// legacy-specific and is the next milestone — the client reaches "Downloading terrain" and waits.
    /// </summary>
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
        // Same lifecycle event as a modern join: ChunkStreamer streams terrain (protocol-aware), and
        // PlayerVisibility spawns this player to modern clients (modern packets) — while the modern
        // packets aimed back at THIS legacy client are dropped by CanEncode. So modern players see the
        // legacy player; the legacy client just doesn't see them yet (its clientbound entity packets
        // are future work).
        // A logged-in legacy client is a normal in-world player — the Play state (its decoding uses a
        // fixed legacy state regardless, so this only affects the protocol-agnostic InWorld checks).
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

        var status = new {
            version = new { name = protocol.VersionName, protocol = protocol.Version },
            players = new { max = server.MaxPlayers, online = server.PlayerCount, sample = Array.Empty<object>() },
            description = new { text = server.MOTD },
        };

        return JsonSerializer.Serialize(status);
    }
}
