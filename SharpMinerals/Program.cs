using System.Collections.Concurrent;
using System.Net;
using SharpMinerals;
using SharpMinerals.Level;
using SharpMinerals.Network;
using SharpMinerals.Network.Handlers;
using SharpMinerals.Network.Protocols.JE763;
using SharpMinerals.Network.Tcp;

const int Port = 25565;

#if TEST_HARNESS
// `dotnet run -- selftest` runs the in-process play-state verification and exits.
if (args.Length > 0 && args[0] == "selftest")
    return SelfTest.Run();

// `dotnet run -- dumpnbt <path>` writes the Join Game registry codec NBT to disk
// for external validation.
if (args.Length > 1 && args[0] == "dumpnbt") {
    File.WriteAllBytes(args[1], SharpMinerals.Network.Nbt.RegistryCodec.Default.ToBytes());
    Console.WriteLine($"Wrote registry codec NBT ({new FileInfo(args[1]).Length} bytes) to {args[1]}");
    return 0;
}

// `dotnet run -- dumpchunk <path>` writes a Chunk Data packet body (column 0,0 of a
// flat world) to disk for external validation.
if (args.Length > 1 && args[0] == "dumpchunk") {
    var w = new World("dump", new FlatChunkGenerator());
    File.WriteAllBytes(args[1], SharpMinerals.Network.ChunkSerializer.Build(w, 0, 0).Payload);
    Console.WriteLine($"Wrote chunk data ({new FileInfo(args[1]).Length} bytes) to {args[1]}");
    return 0;
}
#endif

// Supported protocol versions; each connection picks one by its handshake version.
var protocols = new ProtocolRegistry(new ProtocolJE763());
var endpoint = new IPEndPoint(IPAddress.Any, Port);

// One default world to start with, generated as a superflat.
var worlds = new ConcurrentDictionary<string, World>();
worlds["overworld"] = new World("overworld", new FlatChunkGenerator());

// TCP transport, feeding decoded packets into server logic and despawning
// players when their connection drops.
var netServer = new TcpNetServer(
    endpoint, protocols,
    ServerPacketHandler.Handle,
    client => Server.Instance?.RemovePlayer(client.Id));

var context = new ServerContext {
    NetServer = netServer,
    Worlds = worlds,
    MOTD = "A SharpMinerals server",
    MaxPlayers = 20,
    TicksPerSecond = 20.0,
};

var server = new Server(context);

// Ctrl+C stops the loop cleanly.
Console.CancelKeyPress += (_, e) => {
    e.Cancel = true;
    Console.WriteLine("Shutting down...");
    server.Stop();
};

server.Start();

// Command/chat console: lines from stdin (or a `run` script) drive the server;
// '/cmd' runs a command, plain text broadcasts as chat.
var commands = new SharpMinerals.Commands.CommandDispatcher()
    .Register(new SharpMinerals.Commands.HelpCommand())
    .Register(new SharpMinerals.Commands.RunCommand())
    .Register(new SharpMinerals.Commands.TimeoutCommand())
    .Register(new SharpMinerals.Commands.ServerCommand());
#if TEST_HARNESS
commands.Register(new SharpMinerals.Commands.TestCommand());
commands.Register(new SharpMinerals.Commands.OpenChestCommand());
#endif
server.Commands = commands;
var console = new SharpMinerals.Commands.ConsoleInput(commands);
console.Start();

// Optionally run a startup script.
var startup = Environment.GetEnvironmentVariable("SHARPMINERALS_TESTFILE");
if (!string.IsNullOrEmpty(startup))
    _ = commands.ExecuteAsync(console, "run " + startup);

return 0;
