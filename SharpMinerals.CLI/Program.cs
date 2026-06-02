using Microsoft.Extensions.Logging;
using SharpMinerals;
using SharpMinerals.CLI;
using SharpMinerals.Commands;
using SharpMinerals.Level;
using SharpMinerals.Modding;
using SharpMinerals.Network;
using SharpMinerals.Network.Handlers;
using SharpMinerals.Network.Protocols.JE61;
using SharpMinerals.Network.Protocols.JE763;
using SharpMinerals.Network.Tcp;
using SharpMinerals.Persistence;
using SharpMinerals.Persistence.RocksDb;
using SharpMinerals.SampleMod;
using SharpMinerals.TestMod;
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
// Discover and initialise mods BEFORE the protocols/type-mappers snapshot the block/item/entity palette:
// a mod's OnInitialize registers content, so it must run first. File mods are `mods/*.dll`; the
// test-harness mod (/test) is compiled in and loaded only on Debug builds (TEST_HARNESS).
var modLoader = new ModLoader();
#if TEST_HARNESS
modLoader.TryLoad(new TestMod());
#endif
#if DEBUG
modLoader.TryLoad(new SampleMod());
#endif
modLoader.LoadDirectory(Path.Combine(Directory.GetCurrentDirectory(), "mods"));
ModContent.Freeze(); // seal the palette — no block/item/entity may register past this point

// Supported protocol versions; each connection picks one — modern versions by their handshake
// version, the legacy JE61 (1.5.2) by its non-Netty first byte (see ProtocolRegistry.Detect).
var protocols = new ProtocolRegistry(new ProtocolJE763(), new ProtocolJE61());
var endpoint = new IPEndPoint(IPAddress.Parse(config.Host), config.Port);

// Disk-backed persistence (RocksDB), behind write-behind queues so saves never block hot paths.
// Lives under the configured data dir (relative to the working directory, or absolute) — keep it
// outside bin/ so the world survives rebuilds.
var worldStore = new AsyncWorldStore(new RocksDbWorldStore(Path.Combine(config.DataDir, "chunks")));
var playerStore = new AsyncPlayerStore(new RocksDbPlayerStore(Path.Combine(config.DataDir, "players")));

// The configured main world, generated as a superflat (and persisted to the store).
var worlds = new ConcurrentDictionary<string, World>();
worlds[config.World] = new World(config.World, new FlatChunkGenerator(), worldStore);

// Forward-declared so the transport callbacks below can reach them. They only fire once a client
// connects (after Start), by which point both are assigned — no static singleton needed.
Server server = null!;
ServerPacketHandler packetHandler = null!;

// TCP transport, feeding decoded packets into server logic and despawning players on drop.
var netServer = new TcpNetServer(
    endpoint, protocols,
    (client, message) => packetHandler.Handle(client, message),
    // RemovePlayer despawns the entity under the server's ECS gate, so it's safe to call straight from
    // the network thread without racing the tick.
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
packetHandler = new ServerPacketHandler(server); // the message handler, bound to the server

// Ctrl+C signals shutdown; the main thread (blocked on WaitForShutdown below) does the cleanup.
Console.CancelKeyPress += (_, e) => {
    e.Cancel = true;
    log.LogInformation("Forced shutdown...");
    server.Stop();
};

server.Start();

// Command/chat console: lines from stdin (or a `run` script) drive the server;
// '/cmd' runs a command, plain text broadcasts as chat.
server.CommandDispatcher
    .RegisterHelp()
    .RegisterRun()
    .RegisterTimeout()
    .RegisterServer()
    .RegisterSave()
    .RegisterTp()
    .RegisterWorld()
    .RegisterClear()
    .RegisterGive();

// Mods run their OnServerStarted now (server is up, core commands registered): they layer their own
// commands on top, set the MOTD, subscribe to events, etc. The /test command comes from the test mod.
modLoader.StartAll(server);

// The console: the server owns the sender identity (shared with tests); the CLI just wires its stdin loop to
// it and logs its replies. The sender lives in core; only the byte-level I/O is host-side.
var console = new ConsoleInput(server.Sender);
console.Start();

// Optionally run a startup script.
if (!string.IsNullOrEmpty(config.Startup))
    server.Sender.RunCommand("run " + config.Startup);

// Run for the server's lifetime. Ctrl+C or `/server stop` releases this; then we close the
// stores and return, and — with every other thread now a background thread — the process exits.
server.WaitForShutdown();
modLoader.StopAll(server); // let mods release anything they own before the stores close
playerStore.Dispose();
worldStore.Dispose();
return 0;
