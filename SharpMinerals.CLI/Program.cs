using Microsoft.Extensions.Logging;
using SharpMinerals;
using SharpMinerals.Blocks;
using SharpMinerals.CLI;
using SharpMinerals.Commands;
using SharpMinerals.Vanilla;
using SharpMinerals.Level;
using SharpMinerals.Modding;
using SharpMinerals.Network;
using SharpMinerals.Network.Handlers;
using SharpMinerals.Network.Protocols.JE61;
using SharpMinerals.Network.Protocols.JE762;
using SharpMinerals.Network.Protocols.JE763;
using SharpMinerals.Network.Tcp;
using SharpMinerals.Persistence;
#if !IN_MEMORY
#if AOT
using SharpMinerals.Persistence.MinRocksDb;
#else
using SharpMinerals.Persistence.RocksDb;
#endif
#endif
#if DEBUG
using SharpMinerals.SampleMod;
#endif
#if TEST_HARNESS
using SharpMinerals.TestMod;
#endif
using System.Collections.Concurrent;
using System.Net;

var loaded = ServerConfig.Load(Path.Combine(Directory.GetCurrentDirectory(), "server.json"));
var config = loaded.Config;
LoggingSetup.Configure(config.LogsDir, config.LogLevel);
var log = Logging.For("Bootstrap");

if (loaded.Notice is { } notice) {
    if (loaded.NoticeIsWarning) log.LogWarning("{Notice}", notice);
    else log.LogInformation("{Notice}", notice);
}

// ── Mods ────────────────────────────────────────────────────────────────────
// Initialise mods before the protocols snapshot the palette (OnInitialize registers content). File mods are
// `mods/*.dll`; the test/sample mods are compiled in and loaded only on the matching build flags.
var modLoader = new ModLoader();
// Force the core engine blocks (air id 0, missing id 1) to register before any mod, then load the vanilla
// content mod FIRST so minecraft:* blocks get the lowest palette ids right after the engine primitives.
_ = BlockRegistry.Air;
modLoader.TryLoad(new VanillaMod());
#if TEST_HARNESS
modLoader.TryLoad(new TestMod());
#endif
#if DEBUG
modLoader.TryLoad(new SampleMod());
#endif
#if !AOT
modLoader.LoadDirectory(Path.Combine(Directory.GetCurrentDirectory(), "mods")); // dynamic mod loading is JIT-only
#endif
ModContent.Freeze();  // seal the palette — no block/item/entity may register past this point
TypeMapper.Freeze();  // seal the wire mappings — protocols build their TypeMapper from them on demand

// Supported protocols; each connection picks one (see ProtocolRegistry.Detect).
var protocols = new ProtocolRegistry(new ProtocolJE763(), new ProtocolJE762(), new ProtocolJE61());
var endpoint = new IPEndPoint(IPAddress.Parse(config.Host), config.Port);

// Persistence behind write-behind queues so saves never block hot paths. The backing store is chosen at
// compile time: IN_MEMORY = ephemeral (no disk), AOT = the AOT-safe MinRocksDb binding, else RocksDbSharp.
// MinRocksDb and RocksDbSharp share an on-disk layout, so a world is portable between them.
#if IN_MEMORY
var worldStore = new AsyncWorldStore(new InMemoryWorldStore());
var playerStore = new AsyncPlayerStore(new InMemoryPlayerStore());
#elif AOT
var worldStore = new AsyncWorldStore(new MinRocksDbWorldStore(Path.Combine(config.DataDir, "chunks")));
var playerStore = new AsyncPlayerStore(new MinRocksDbPlayerStore(Path.Combine(config.DataDir, "players")));
#else
var worldStore = new AsyncWorldStore(new RocksDbWorldStore(Path.Combine(config.DataDir, "chunks")));
var playerStore = new AsyncPlayerStore(new RocksDbPlayerStore(Path.Combine(config.DataDir, "players")));
#endif

// The configured main world, a persisted superflat.
var worlds = new ConcurrentDictionary<string, World>();
worlds[config.World] = new World(config.World, new FlatChunkGenerator(), worldStore);

// Forward-declared for the transport callbacks below; both are assigned before any client connects.
Server server = null!;
ServerPacketHandler packetHandler = null!;

var netServer = new TcpNetServer(
    endpoint, protocols,
    (client, message) => packetHandler.Handle(client, message),
    // RemovePlayer despawns under the ECS gate, so it's safe to call from the network thread.
    client => server.RemovePlayer(client.Id));

var context = new ServerContext {
    NetServer = netServer,
    Worlds = worlds,
    MOTD = config.Motd,
    MaxPlayers = config.MaxPlayers,
    TicksPerSecond = config.Tps,
    PlayerStore = playerStore,
};

server = new Server(context);
packetHandler = new ServerPacketHandler(server);

// Ctrl+C signals shutdown; the main thread (blocked on WaitForShutdown below) does the cleanup.
Console.CancelKeyPress += (_, e) => {
    e.Cancel = true;
    log.LogInformation("Forced shutdown...");
    server.Stop();
};

server.Start();

// ── Commands ────────────────────────────────────────────────────────────────
server.CommandDispatcher
    .RegisterHelp()
    .RegisterRun()
    .RegisterTimeout()
    .RegisterServer()
    .RegisterSave()
    .RegisterTp()
    .RegisterWorld()
    .RegisterClear()
    .RegisterGive()
    .RegisterSummon();

// Mods run OnServerStarted now (server up, core commands registered): they layer on their own commands, etc.
modLoader.StartAll(server);

// Console: the server owns the sender (shared with tests); the CLI wires its stdin loop and logs replies.
var console = new ConsoleInput(server.Sender);
console.Start();

// Optionally run a startup script.
if (!string.IsNullOrEmpty(config.Startup))
    server.Sender.RunCommand("run " + config.Startup);

// Block for the server's lifetime; Ctrl+C or `/server stop` releases this, then we close the stores and exit.
server.WaitForShutdown();
modLoader.StopAll(server); // let mods release anything they own before the stores close
playerStore.Dispose();
worldStore.Dispose();
return 0;
