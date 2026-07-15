using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using SharpMinerals.Chat;
using SharpMinerals.Entities.Components;
using SharpMinerals.Level;
using SharpMinerals.Network;
using SharpMinerals.Network.Buffers;
using SharpMinerals.Network.Handlers;
using SharpMinerals.Network.Messages;
using SharpMinerals.Network.Protocols.JE61;
using SharpMinerals.Network.Protocols.JE762;
using SharpMinerals.Network.Protocols.JE763;
using SharpMinerals.Network.Tcp;
using SharpMinerals.Vanilla;
using Xunit;
using World = SharpMinerals.Level.World;

namespace SharpMinerals.Tests;

/// <summary>
/// End-to-end over a REAL TCP socket: brings up the actual <see cref="TcpNetServer"/> (background accept +
/// receive threads, the two-lane <c>TcpNetClient</c> writer, real byte framing and the running tick loop), then
/// connects two raw clients that speak just enough 1.20.1 (763) to log in. This exercises the layer the in-memory
/// CaptureNetClient tests skip - so it catches a player spawn that is dropped or reordered ON THE WIRE, which is
/// the only place a "players invisible, entities fine" regression could still hide.
/// </summary>
public sealed class EntityTrackerWireTests {
    const int SpawnPlayerId = 0x03; // named_entity_spawn (1.20.1)

    [Fact]
    public void TwoRealClientsReceiveEachOthersSpawnOverTheWire() {
        int port = FreePort();
        var registry = new ProtocolRegistry(new ProtocolJE763(), new ProtocolJE762(), new ProtocolJE61());
        var endpoint = new IPEndPoint(IPAddress.Loopback, port);

        Server server = null!;
        ServerPacketHandler handler = null!;
        var netServer = new TcpNetServer(endpoint, registry,
            (client, message) => handler.Handle(client, message),
            client => server.RemovePlayer(client.Id));

        var worlds = new ConcurrentDictionary<string, World> {
            ["overworld"] = new World("overworld", new FlatChunkGenerator()),
        };
        server = new Server(new ServerContext { Worlds = worlds, MOTD = new TextComponent("wire"), MaxPlayers = 8, TicksPerSecond = 20,
        }, netServer);
        handler = new ServerPacketHandler(server);
        server.Start();

        RawClient? alice = null, bob = null;
        try {
            alice = RawClient.Connect(port, "Alice");
            bob = RawClient.Connect(port, "Bob");

            // Both spawn at the same column (0,0); the tracker should spawn each to the other within a few ticks.
            int aliceEid = WaitForEid(server, "Alice");
            int bobEid = WaitForEid(server, "Bob");
            var aliceUuid = ServerPacketHandler.OfflineUuid("Alice");
            var bobUuid = ServerPacketHandler.OfflineUuid("Bob");

            Assert.True(alice.WaitForSpawnPlayer(bobEid, TimeSpan.FromSeconds(5)),
                "Alice's client received Bob's SpawnPlayer over the wire");
            Assert.True(bob.WaitForSpawnPlayer(aliceEid, TimeSpan.FromSeconds(5)),
                "Bob's client received Alice's SpawnPlayer over the wire");

            // A 1.20.1 client drops a SpawnPlayer whose UUID has no player-list (tab) entry yet. Verify on the WIRE
            // that the tab entry arrived AND preceded the spawn - the ordering the rewrite changed.
            Assert.True(alice.PlayerInfoBeforeSpawn(bobUuid, bobEid), "Alice got Bob's tab entry before his spawn");
            Assert.True(bob.PlayerInfoBeforeSpawn(aliceUuid, aliceEid), "Bob got Alice's tab entry before her spawn");
        } finally {
            alice?.Dispose();
            bob?.Dispose();
            server.Stop();
        }
    }

    static int WaitForEid(Server server, string name) {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (DateTime.UtcNow < deadline) {
            foreach (var (_, ctx) in server.Players)
                if (ctx.World.Ecs.IsAlive(ctx.Entity)) {
                    var np = ctx.World.Ecs.Get<PlayerEntityComponent>(ctx.Entity);
                    if (np.Name == name) return np.NetId;
                }
            Thread.Sleep(25);
        }
        throw new InvalidOperationException($"player '{name}' never joined");
    }

    static int FreePort() {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        int port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }

    /// <summary>A minimal 1.20.1 client: handshake + login start, then a background thread that frames every
    /// inbound packet and records the entity ids of any SpawnPlayer (0x03) it sees. No encryption/compression
    /// (modern offline mode uses neither), and it never confirms the join teleport - it only needs to stand at
    /// spawn and listen, which is enough to observe whether the other player's spawn arrives.</summary>
    sealed class RawClient : IDisposable {
        const int PlayerInfoUpdateId = 0x3A; // player_info_update (1.20.1)
        const byte AddPlayerAction = 0x01;

        readonly TcpClient tcp;
        readonly Thread reader;
        readonly ConcurrentDictionary<int, bool> spawnedPlayers = new();
        // Ordered wire log of the interesting packets: ("info", uuid) and ("spawn", eid as guid-less marker).
        readonly object logLock = new();
        readonly List<(string Kind, Guid Uuid, int Eid)> log = new();
        volatile bool open = true;

        RawClient(TcpClient tcp) {
            this.tcp = tcp;
            reader = new Thread(ReadLoop) { IsBackground = true };
            reader.Start();
        }

        public static RawClient Connect(int port, string name) {
            var tcp = new TcpClient();
            tcp.Connect(IPAddress.Loopback, port);
            var protocol = new ProtocolJE763();
            var stream = tcp.GetStream();
            Send(stream, protocol, new HandshakeC2S(763, "localhost", (ushort)port, NextState: 2));
            Send(stream, protocol, new LoginStartC2S(name, Guid.NewGuid()));
            return new RawClient(tcp);
        }

        static void Send(NetworkStream stream, ProtocolJE763 protocol, IMessage message) {
            byte[] frame = protocol.Frame(message);
            lock (stream) { stream.Write(frame, 0, frame.Length); stream.Flush(); }
        }

        void ReadLoop() {
            try {
                var ms = new MinecraftStream(tcp.GetStream());
                while (open) {
                    int length = ms.ReadVarInt();
                    if (length <= 0) continue;
                    byte[] body = ms.ReadBytes(length);
                    var bs = new MinecraftStream(new MemoryStream(body, writable: false));
                    int id = bs.ReadVarInt();
                    if (id == SpawnPlayerId) {
                        int eid = bs.ReadVarInt();        // entity id follows the packet id
                        spawnedPlayers[eid] = true;
                        lock (logLock) log.Add(("spawn", Guid.Empty, eid));
                    } else if (id == PlayerInfoUpdateId) {
                        byte actions = bs.ReadUByte();
                        if ((actions & AddPlayerAction) == 0) continue; // not an add - no profile to learn
                        int count = bs.ReadVarInt();
                        for (int i = 0; i < count; i++) {
                            var uuid = bs.ReadUuid();     // entries start with the UUID (ADD_PLAYER ordinal)
                            lock (logLock) log.Add(("info", uuid, 0));
                            break;                        // first entry's UUID is enough for our assertions
                        }
                    }
                }
            } catch { /* socket closed or stream ended - done reading */ }
        }

        public bool WaitForSpawnPlayer(int entityId, TimeSpan timeout) {
            var deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline) {
                if (spawnedPlayers.ContainsKey(entityId)) return true;
                Thread.Sleep(25);
            }
            return false;
        }

        /// <summary>Whether the wire log shows the player's tab entry (PlayerInfoUpdate add for <paramref name="uuid"/>)
        /// arriving strictly before its entity spawn (SpawnPlayer for <paramref name="eid"/>).</summary>
        public bool PlayerInfoBeforeSpawn(Guid uuid, int eid) {
            lock (logLock) {
                int info = log.FindIndex(e => e.Kind == "info" && e.Uuid == uuid);
                int spawn = log.FindIndex(e => e.Kind == "spawn" && e.Eid == eid);
                return info >= 0 && spawn >= 0 && info < spawn;
            }
        }

        public void Dispose() {
            open = false;
            try { tcp.Close(); } catch { /* already gone */ }
        }
    }
}
