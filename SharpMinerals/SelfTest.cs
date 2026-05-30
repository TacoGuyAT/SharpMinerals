using System.Collections.Concurrent;
using Arch.Core;
using SharpMinerals.Blocks;
using SharpMinerals.Components;
using SharpMinerals.Entities.Components;
using SharpMinerals.Items;
using SharpMinerals.Level;
using SharpMinerals.Math;
using SharpMinerals.Network;
using SharpMinerals.Network.Buffers;
using SharpMinerals.Network.Handlers;
using SharpMinerals.Network.Messages;
using SharpMinerals.Network.Protocols.JE763;
using World = SharpMinerals.Level.World;

namespace SharpMinerals;

/// <summary>
/// In-process verification of the play state: flat generation, block break/place,
/// item drops, codec round-trips, and the login → dig → place packet handlers
/// driven through an in-memory transport. Run with <c>dotnet run -- selftest</c>.
/// </summary>
public static class SelfTest {
    public static int Run() {
        int failures = 0;
        void Check(string name, bool condition) {
            Console.WriteLine($"  [{(condition ? "PASS" : "FAIL")}] {name}");
            if (!condition) failures++;
        }

        var protocol = new ProtocolJE763();

        // ── Flat generation ────────────────────────────────────────────────
        var gen = new World("gen", new FlatChunkGenerator());
        Check("flat: bedrock at y=0", gen.GetBlock(new Vector3i(0, 0, 0)) == BlockRegistry.Bedrock);
        Check("flat: dirt at y=2", gen.GetBlock(new Vector3i(0, 2, 0)) == BlockRegistry.Dirt);
        Check("flat: grass at y=4", gen.GetBlock(new Vector3i(0, 4, 0)) == BlockRegistry.GrassBlock);
        Check("flat: air at y=5", gen.GetBlock(new Vector3i(0, 5, 0)).IsAir);
        Check("flat: works at negative coords", gen.GetBlock(new Vector3i(-1, 0, -33)) == BlockRegistry.Bedrock);

        // ── Block break + drop ──────────────────────────────────────────────
        int before = DropCount(gen);
        var broken = gen.BreakBlock(new Vector3i(5, 4, 5));
        Check("break: returned the grass block", broken == BlockRegistry.GrassBlock);
        Check("break: space is now air", gen.GetBlock(new Vector3i(5, 4, 5)).IsAir);
        Check("break: spawned one drop entity", DropCount(gen) == before + 1);
        Check("break: air yields nothing", gen.BreakBlock(new Vector3i(5, 50, 5)).IsAir);

        // ── Block placement ─────────────────────────────────────────────────
        Check("place: into air succeeds", gen.PlaceBlock(new Vector3i(5, 5, 5), BlockRegistry.Stone));
        Check("place: block was set", gen.GetBlock(new Vector3i(5, 5, 5)) == BlockRegistry.Stone);
        Check("place: into a solid fails", !gen.PlaceBlock(new Vector3i(5, 5, 5), BlockRegistry.Dirt));

        // ── Position packing ────────────────────────────────────────────────
        foreach (var p in new[] {
            new Vector3i(0, 0, 0), new Vector3i(1, 2, 3),
            new Vector3i(-30_000_000, -2000, 30_000_000),
        }) {
            using var ms = new MinecraftStream(new MemoryStream());
            ms.WritePosition(p.X, p.Y, p.Z);
            ms.Position = 0;
            var (x, y, z) = ms.ReadPosition();
            Check($"position packs {p}", x == p.X && y == p.Y && z == p.Z);
        }

        // ── Codec round-trips ───────────────────────────────────────────────
        Check("round-trip Handshake", RoundTrip(protocol, ConnectionState.Handshaking,
            new HandshakeC2S(763, "localhost", 25565, 2)));
        Check("round-trip PlayerAction", RoundTrip(protocol, ConnectionState.Play,
            new PlayerActionC2S(2, new Vector3i(10, -5, 20), 1, 7)));
        Check("round-trip UseItemOn", RoundTrip(protocol, ConnectionState.Play,
            new UseItemOnC2S(0, new Vector3i(1, 2, 3), 1, 0.5f, 0.25f, 0.75f, false, 9)));
        Check("round-trip SetPlayerPosition", RoundTrip(protocol, ConnectionState.Play,
            new SetPlayerPositionC2S(1.5, 64.0, -3.5, true)));
        Check("round-trip KeepAlive", RoundTrip(protocol, ConnectionState.Play,
            new KeepAliveC2S(123_456_789L)));

        // ── Handler flow over an in-memory transport ────────────────────────
        var capture = new CaptureNetServer(protocol);
        var worlds = new ConcurrentDictionary<string, World> {
            ["overworld"] = new World("overworld", new FlatChunkGenerator()),
        };
        var server = new Server(new ServerContext {
            NetServer = capture, Worlds = worlds, MOTD = "self-test", MaxPlayers = 20, TicksPerSecond = 20,
        });
        var client = new CaptureNetClient(1, protocol) { State = ConnectionState.Login };
        capture.Register(client);

        // Login.
        ServerPacketHandler.Handle(client, new LoginStartC2S("Steve", Guid.Empty));
        Check("login: switched to Play", client.State == ConnectionState.Play);
        Check("login: sent LoginSuccess", client.Sent.Any(m => m is LoginSuccessS2C));
        Check("login: sent JoinGame", client.Sent.Any(m => m is JoinGameS2C));
        Check("login: sent Set Center Chunk", client.Sent.Any(m => m is SetCenterChunkS2C));
        Check("login: streamed chunk data", client.Sent.OfType<ChunkDataS2C>().Any());
        Check("login: sent SetHealth", client.Sent.Any(m => m is SetHealthS2C));
        Check("login: player spawned", server.PlayerCount == 1);
        Check("login: offline UUID is deterministic + non-empty",
            ServerPacketHandler.OfflineUuid("Steve") == ServerPacketHandler.OfflineUuid("Steve") &&
            ServerPacketHandler.OfflineUuid("Steve") != Guid.Empty);

        // Digging (creative instant break, status 0).
        client.Sent.Clear();
        capture.Broadcasts.Clear();
        var digPos = new Vector3i(0, 4, 0);
        Check("dig: target starts as grass", server.DefaultWorld.GetBlock(digPos) == BlockRegistry.GrassBlock);
        ServerPacketHandler.Handle(client, new PlayerActionC2S(0, digPos, 1, 42));
        Check("dig: block removed", server.DefaultWorld.GetBlock(digPos).IsAir);
        Check("dig: acknowledged sequence", client.Sent.Any(m => m is AckBlockChangeS2C a && a.Sequence == 42));
        Check("dig: BlockUpdate broadcast",
            capture.Broadcasts.Any(m => m is BlockUpdateS2C b && b.Position == digPos && b.BlockStateId == TypeMapper.StateId(BlockRegistry.Air)));
        Network.Handlers.DropSystem.Tick(server); // drops are announced by the per-tick drop system
        Check("dig: drop announced (SpawnEntity)", capture.Broadcasts.Any(m => m is SpawnEntityS2C));

        // Placement (held item is Stone; placing on the top face of a grass block).
        capture.Broadcasts.Clear();
        var placeOn = new Vector3i(2, 4, 2);
        var placedAt = new Vector3i(2, 5, 2);
        ServerPacketHandler.Handle(client, new UseItemOnC2S(0, placeOn, (int)BlockFace.Top, 0.5f, 1f, 0.5f, false, 43));
        Check("place: stone placed above clicked face", server.DefaultWorld.GetBlock(placedAt) == BlockRegistry.Stone);
        Check("place: BlockUpdate broadcast",
            capture.Broadcasts.Any(m => m is BlockUpdateS2C b && b.Position == placedAt && b.BlockStateId == TypeMapper.StateId(BlockRegistry.Stone)));

        // ── Containers: open a chest, move an item in, sync to a second viewer ──
        client.Sent.Clear();
        var chestEntity = new BlockEntity(new Vector3i(10, 5, 10), BlockRegistry.Chest);
        server.DefaultWorld.SetBlockEntity(chestEntity);
        server.Containers.Open(server, client.Id, chestEntity);
        Check("container: open sent OpenScreen", client.Sent.OfType<OpenScreenS2C>().Any());
        Check("container: open sent Content", client.Sent.OfType<SetContainerContentS2C>().Any());

        int win = client.Sent.OfType<OpenScreenS2C>().First().WindowId;
        // Pick up the hotbar stone (chest-window slot 54 = Main(0)) then drop it into chest slot 0.
        server.Containers.OnClick(server, client.Id, new ClickContainerC2S(win, 0, 54, 0, 0));
        server.Containers.OnClick(server, client.Id, new ClickContainerC2S(win, 1, 0, 0, 0));
        var chestInv = chestEntity.Get<Inventory>();
        Check("container: stone moved into chest", !chestInv[0].IsEmpty && chestInv[0].Type == BlockRegistry.Stone);

        // A second viewer of the same chest is synced when the first viewer clicks.
        var viewer = new CaptureNetClient(2, protocol) { State = ConnectionState.Play };
        capture.Register(viewer);
        server.AddPlayer(viewer, "Steve2", ServerPacketHandler.OfflineUuid("Steve2"));
        server.Containers.Open(server, viewer.Id, chestEntity);
        viewer.Sent.Clear();
        server.Containers.OnClick(server, client.Id, new ClickContainerC2S(win, 2, 0, 0, 0)); // #1 picks the stack back up
        Check("container: second viewer synced", viewer.Sent.OfType<SetContainerContentS2C>().Any());
        server.RemovePlayer(viewer.Id);

        // ── Item pickup: a dropped stack near the player is collected via collision ──
        var dropEntity = server.DefaultWorld.SpawnDroppedItem(new Vector3i(0, 5, 0), new ItemStack(BlockRegistry.Cobblestone, 1));
        for (int i = 0; i < 12; i++) server.DefaultWorld.Tick(); // age past pickup delay, settle, detect collision
        Network.Handlers.DropSystem.Tick(server);                // announce + pick up
        Check("pickup: drop entity removed", !server.DefaultWorld.Ecs.IsAlive(dropEntity));
        server.TryGetPlayer(client.Id, out var pickHandle);
        var pickInv = server.DefaultWorld.Ecs.Get<EntityInventory>(pickHandle.Entity);
        bool hasCobble = false;
        for (int s = 0; s < EntityInventory.MainSize; s++)
            if (pickInv.Main(s).Type == BlockRegistry.Cobblestone) hasCobble = true;
        Check("pickup: item added to inventory", hasCobble);

        // Disconnect cleanup.
        server.RemovePlayer(client.Id);
        Check("disconnect: player despawned", server.PlayerCount == 0);

        Console.WriteLine(failures == 0
            ? "\nALL SELF-TESTS PASSED"
            : $"\n{failures} SELF-TEST(S) FAILED");
        return failures == 0 ? 0 : 1;
    }

    static int DropCount(World world) =>
        world.Ecs.CountEntities(in new QueryDescription().WithAll<DroppedItem>());

    static bool RoundTrip(Protocol protocol, ConnectionState state, IMessage message) {
        // EncodePayload writes [VarInt id][body] using the type's registered codec.
        var payload = protocol.EncodePayload(message);
        using var ms = new MinecraftStream(new MemoryStream(payload, writable: false));
        int id = ms.ReadVarInt();
        var codec = protocol.CodecFor(state, PacketDirection.Serverbound, id);
        return codec is not null && codec.Decode(ms).Equals(message);
    }

    // ── In-memory transport doubles ─────────────────────────────────────────
    sealed class CaptureNetClient : NetClient {
        public readonly List<IMessage> Sent = new();
        public CaptureNetClient(ulong id, Protocol protocol) : base(id, protocol) { }
        public override void Send(IMessage message) => Sent.Add(message);
        public override void Disconnect() { }
    }

    sealed class CaptureNetServer : INetServer {
        public readonly List<IMessage> Broadcasts = new();
        readonly List<NetClient> clients = new();
        public ProtocolRegistry Registry { get; }
        public Protocol Protocol => Registry.Default;
        public CaptureNetServer(Protocol protocol) => Registry = new ProtocolRegistry(protocol);
        public void Register(NetClient client) => clients.Add(client);
        public void Start() { }
        public void Stop() { }
        public void Send(ulong client, IMessage message) {
            foreach (var c in clients) if (c.Id == client) c.Send(message);
        }
        public void Broadcast(IMessage message, Func<NetClient, bool>? predicate = null) {
            Broadcasts.Add(message);
            foreach (var c in clients) if (predicate is null || predicate(c)) c.Send(message);
        }
    }
}
