#if TEST_HARNESS
using System.Collections.Concurrent;
using System.Net;
using SharpMinerals.Commands;
using SharpMinerals.Modding;
using SharpMinerals.Events;
using SharpMinerals.Events.Contexts;
using SharpMinerals.Level;
using SharpMinerals.Vanilla;
using SharpMinerals.Network;
using SharpMinerals.Network.Handlers;
using SharpMinerals.Network.Protocols.JE61;
using SharpMinerals.Network.Protocols.JE762;
using SharpMinerals.Network.Protocols.JE763;
using SharpMinerals.Network.Tcp;
using Xunit;

namespace SharpMinerals.Tests.RealClient;

/// <summary>
/// Brings up a REAL in-process server (full TCP transport) and waits for an actual Minecraft client — the
/// SharpTester mod — to connect. It launches nothing: you start a client on the host (or elsewhere) and point
/// it at the server. Tests then drive that client through the EXISTING <c>/test</c> command and assert on what
/// the client reports back over the control channel. Each test runs in its own world; the previous one is
/// unloaded. Gated by the <c>SHARPMINERALS_REALCLIENT</c> env var so a normal <c>dotnet test</c> never waits.
/// </summary>
public sealed class RealClientFixture : IAsyncLifetime {
    public const string EnableVar = "SHARPMINERALS_REALCLIENT";
    public static bool Enabled => Environment.GetEnvironmentVariable(EnableVar) == "1";

    public Server Server { get; private set; } = null!;
    public ulong ClientId { get; private set; }
    public string PlayerName { get; private set; } = "";
    public World Lobby { get; private set; } = null!;

    CommandDispatcher dispatcher = null!;
    readonly ConcurrentDictionary<ulong, TaskCompletionSource<string>> pending = new();
    World? previousWorld;

    static int Port => int.TryParse(Environment.GetEnvironmentVariable("SHARPMINERALS_PORT"), out var p) ? p : 25565;
    static int JoinTimeoutSec => int.TryParse(Environment.GetEnvironmentVariable("SHARPMINERALS_JOIN_TIMEOUT"), out var s) ? s : 180;

    public async Task InitializeAsync() {
        if (!Enabled) return; // tests are skipped — don't stand up a server or wait for a client

        // Mirror the CLI's wiring: a real TCP transport so a real client can connect, feeding decoded packets
        // into the server and despawning players on drop.
        var protocols = new ProtocolRegistry(new ProtocolJE763(), new ProtocolJE762(), new ProtocolJE61());
        var endpoint = new IPEndPoint(IPAddress.Any, Port);
        Server server = null!;
        ServerPacketHandler packetHandler = null!;
        var netServer = new TcpNetServer(
            endpoint, protocols,
            (client, message) => packetHandler.Handle(client, message),
            client => server.RemovePlayer(client.Id));

        var worlds = new ConcurrentDictionary<string, World> {
            ["overworld"] = new World("overworld", new FlatChunkGenerator()),
        };
        server = new Server(new ServerContext {
            NetServer = netServer, Worlds = worlds, MOTD = "Harness", MaxPlayers = 8, TicksPerSecond = 20,
        });
        packetHandler = new ServerPacketHandler(server);
        Server = server;
        dispatcher = server.CommandDispatcher;

        // Register the real command set (so the client receives the full Declare Commands tree and can
        // tab-complete). The harness `/test` command used to drive scenarios now comes from the test-harness
        // mod, loaded through the real ModLoader — so the fixture exercises the same mod path as the CLI.
        dispatcher.RegisterHelp().RegisterRun().RegisterTimeout().RegisterServer()
                  .RegisterSave().RegisterTp().RegisterWorld().RegisterClear().RegisterGive().RegisterSummon();
        var mods = new ModLoader();
        mods.LoadFrom(typeof(SharpMinerals.TestMod.TestMod).Assembly);
        mods.StartAll(server);

        // A client's control-channel reply completes the matching pending request (one outstanding per client).
        server.TestClientReply += (clientId, reply) => {
            if (pending.TryRemove(clientId, out var tcs)) tcs.TrySetResult(reply);
        };

        // First join → capture the client and the world it landed in (the lobby we park it in between tests).
        var joined = new TaskCompletionSource<PlayerContext>(TaskCreationOptions.RunContinuationsAsynchronously);
        server.Events.Subscribe<PlayerJoined>(e => joined.TrySetResult(e.Context));

        server.Start();

        PlayerContext ctx;
        try {
            ctx = await joined.Task.WaitAsync(TimeSpan.FromSeconds(JoinTimeoutSec));
        } catch (TimeoutException) {
            throw new InvalidOperationException(
                $"No client connected within {JoinTimeoutSec}s. Start a SharpTester client and point it at localhost:{Port}.");
        }
        ClientId = ctx.Client.Id;
        PlayerName = ctx.World.Ecs.Get<Entities.Components.NetPlayerEntityComponent>(ctx.Entity).Name;
        Lobby = ctx.World;
        await Task.Delay(1500); // let initial chunks settle
    }

    public Task DisposeAsync() {
        // We launched no client process, so there is nothing external to stop — only the in-process server.
        if (previousWorld is not null) Server?.UnloadWorld(previousWorld);
        Server?.Stop();
        return Task.CompletedTask;
    }

    /// <summary>Switches the client into a fresh per-test world and unloads the PREVIOUS test's world (now
    /// empty). Each test calls this at its start: "create a new world, delete the previous one."</summary>
    public async Task<World> EnterFreshWorld(string name) {
        var world = Server.GetOrCreateWorld(name, static (name, server) => new World(name, new FlatChunkGenerator()));
        Server.SwitchWorld(ClientId, world);
        await Task.Delay(2500); // the client Respawns + reloads chunks into the new world
        if (previousWorld is not null)
            Server.UnloadWorld(previousWorld);
        previousWorld = world;
        return world;
    }

    /// <summary>Runs a harness command on the client via the existing <c>/test</c> command and returns its
    /// single reply (e.g. "count item = 1"). Times out so a hung client can't wedge the run.</summary>
    public async Task<string> Send(string command) {
        var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        pending[ClientId] = tcs;
        await Server.Sender.RunCommandAsync(dispatcher, $"test @{ClientId} {command}");
        return await tcs.Task.WaitAsync(TimeSpan.FromSeconds(15));
    }

    /// <summary>How many entities the client currently renders whose type id contains "item".</summary>
    public async Task<int> CountItems() {
        var reply = await Send("count item");
        int eq = reply.LastIndexOf('=');
        return eq >= 0 && int.TryParse(reply[(eq + 1)..].Trim(), out var n)
            ? n
            : throw new InvalidOperationException($"could not parse count reply: '{reply}'");
    }
}
#endif
