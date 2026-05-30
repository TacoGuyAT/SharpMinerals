using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using SharpMinerals.Entities.Components;
using SharpMinerals.Level;
using SharpMinerals.Network;
using SharpMinerals.Network.Messages;
using PrecisionClock = PrecisionTimer.PrecisionTimer;
using ArchEntity = Arch.Core.Entity;

namespace SharpMinerals;

/// <summary>
/// The top-level server. Drives a fixed-rate game loop on its own thread using
/// <see cref="PrecisionClock"/> and ticks every world each tick. The transport
/// (<see cref="NetServer"/>) runs its own threads and feeds decoded messages into
/// server logic.
/// </summary>
public class Server : ITickable {
    static readonly ILogger Log = Logging.For("Server");

    static Server? instance;
    public static Server? Instance => instance;

    /// <summary>True while the game loop is running.</summary>
    public static bool IsRunning => instance is { running: true };

    ServerContext context;
    volatile bool running;
    Thread? loopThread;
    long currentTick;
    int nextEntityId;

    /// <summary>Allocates a fresh network entity id for spawned entities.</summary>
    public int NextEntityId() => Interlocked.Increment(ref nextEntityId);

    /// <summary>Maps a connected client to the world + entity that represents its player.</summary>
    readonly ConcurrentDictionary<ulong, PlayerHandle> players = new();

    public readonly record struct PlayerHandle(World World, ArchEntity Entity);

    public INetServer NetServer => context.NetServer;

    /// <summary>The command dispatcher, so any <see cref="Commands.IChatSender"/> can issue commands.</summary>
    public Commands.CommandDispatcher? Commands { get; set; }

    /// <summary>Open container windows (chests) and their multiplayer sync.</summary>
    public Network.Containers.ContainerManager Containers { get; } = new();

    public ConcurrentDictionary<string, World> Worlds => context.Worlds;
    public string MOTD { get => context.MOTD; set => context.MOTD = value; }
    public int MaxPlayers => context.MaxPlayers;
    public double TicksPerSecond => context.TicksPerSecond;

    /// <summary>The number of ticks elapsed since <see cref="Start"/>.</summary>
    public long CurrentTick => Interlocked.Read(ref currentTick);

    /// <summary>Players summed across every world.</summary>
    public int PlayerCount => Worlds.Values.Sum(w => w.PlayerCount);

    public Server(ServerContext ctx) {
        instance = this;
        context = ctx;
        if (context.TicksPerSecond <= 0)
            context.TicksPerSecond = 20.0;
    }

    public World DefaultWorld => Worlds.Values.First();

    /// <summary>Starts the transport and launches the tick loop thread.</summary>
    public void Start() {
        if (running) return;
        running = true;

        NetServer.Start();

        loopThread = new Thread(RunLoop) {
            Name = "SharpMinerals Tick Loop",
            IsBackground = false,
        };
        loopThread.Start();

        Log.LogInformation("Server started — {Tps} TPS, MOTD: \"{Motd}\"", TicksPerSecond, MOTD);
    }

    /// <summary>Signals the loop to stop and tears down the transport.</summary>
    public void Stop() {
        running = false;
        NetServer.Stop();
        loopThread?.Join(TimeSpan.FromSeconds(5));
    }

    /// <summary>
    /// The heart of the server. PrecisionTimer paces the loop to a steady rate
    /// (50 ms at 20 TPS) without busy-waiting on platforms that support it.
    /// </summary>
    void RunLoop() {
        var timer = new PrecisionClock(1000.0 / TicksPerSecond);
        while (running) {
            try {
                Tick();
            } catch (Exception ex) {
                Log.LogError(ex, "Tick {Tick} failed", CurrentTick);
            }
            Interlocked.Increment(ref currentTick);
            timer.Wait();
        }
    }

    public void Tick() {
        // Worlds are independent, so they can tick in parallel.
        Worlds.Values.AsParallel().ForAll(w => w.Tick());

        // Item-entity networking (announce new drops, handle pickups) after worlds tick.
        Network.Handlers.DropSystem.Tick(this);

        // Keep play connections alive once a second.
        if (currentTick % (long)TicksPerSecond == 0)
            NetServer.Broadcast(new KeepAliveS2C(currentTick), c => c.State == ConnectionState.Play);
    }

    /// <summary>Every connected player (client id → world + entity).</summary>
    public IEnumerable<KeyValuePair<ulong, PlayerHandle>> Players => players;

    /// <summary>Spawns a player for a freshly logged-in client and returns its network entity id.</summary>
    public int AddPlayer(NetClient client, string name, Guid uuid) {
        var world = DefaultWorld;
        int entityId = NextEntityId();
        var entity = world.SpawnPlayer(client.Id, name, uuid, entityId);
        players[client.Id] = new PlayerHandle(world, entity);
        Log.LogInformation("{Name} (#{Client}, eid {Eid}) joined — {Online} online", name, client.Id, entityId, PlayerCount);
        return entityId;
    }

    /// <summary>Despawns a disconnected client's player entity and tells others it left.</summary>
    public void RemovePlayer(ulong clientId) {
        Containers.OnLeave(clientId);
        if (!players.TryRemove(clientId, out var handle))
            return;
        if (handle.World.Ecs.IsAlive(handle.Entity)) {
            var info = handle.World.Ecs.Get<NetworkedPlayer>(handle.Entity);
            PlayerVisibility.OnLeave(this, info);
            handle.World.Ecs.Destroy(handle.Entity);
        }
    }

    /// <summary>Looks up the world + entity backing a connected client.</summary>
    public bool TryGetPlayer(ulong clientId, out PlayerHandle handle) =>
        players.TryGetValue(clientId, out handle);
}
