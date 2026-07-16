using Microsoft.Extensions.Logging;
using SharpMinerals.Chat;
using SharpMinerals.Commands;
using SharpMinerals.Entities.Components;
using SharpMinerals.Events;
using SharpMinerals.Events.Contexts;
using SharpMinerals.Level;
using SharpMinerals.Math;
using SharpMinerals.Network;
using SharpMinerals.Network.Containers;
using SharpMinerals.Network.Messages;
using SharpMinerals.Persistence;
using System.Collections.Concurrent;
using ArchEntity = Arch.Core.Entity;
using PrecisionClock = PrecisionTimer.PrecisionTimer;

namespace SharpMinerals;

/// <summary>The top-level server: drives a fixed-rate game loop on its own thread and ticks every world.
/// The transport (<see cref="NetServer"/>) runs its own threads and feeds decoded messages into server logic.</summary>
public class Server : ITickable {
    static readonly ILogger Log = Logging.For("Server");

    public bool IsRunning => running;

    ServerContext context;
    readonly IEntityStore entityStore;
    volatile bool running;
    int stopping; // guard so Stop() tears down exactly once
    readonly ManualResetEventSlim stopped = new(false);
    Thread? loopThread;
    long currentTick;
    readonly TpsTracker tps;
    int nextEntityId;
    int nextTeleportId;

    // Clients with an unconfirmed teleport: their position updates are ignored until they confirm the id.
    readonly ConcurrentDictionary<ulong, int> pendingTeleports = new();

    public int NextEntityId() => Interlocked.Increment(ref nextEntityId);

    /// <summary>Maps a connected client to the world + entity that represents its player.</summary>
    readonly ConcurrentDictionary<ulong, PlayerContext> players = new();

    /// <summary>Serializes ECS structural changes (player spawn/despawn on network threads) against the
    /// tick's entity systems, so a join/leave can't race the simulation's queries.</summary>
    readonly object ecsGate = new();

    public INetServer NetServer { get; }

    /// <summary>Sends a message to an audience of clients (default: every in-world client). The transport
    /// encodes once per protocol version and drops the message for any client whose protocol can't encode it.</summary>
    public void BroadcastMessage(IMessage message, Func<NetClient, bool>? audience = null) =>
        NetServer.Broadcast(message, audience ?? (static c => c.InWorld));

    /// <summary>Broadcasts a message only to in-world clients whose player is within range of a world position,
    /// measured horizontally (x, z) since visibility is column-based. <paramref name="radius"/> (blocks) defaults
    /// to each recipient's own chunk-view radius plus a chunk of margin, so a client receives the event exactly
    /// when the affected column is within its loaded view. Use for transient, position-local updates (block
    /// changes, effects) where a client outside its view neither holds the chunk nor needs the packet - not for
    /// entity spawns, which a player must still receive to render an entity it later approaches.</summary>
    public void BroadcastInRange(World world, double x, double z, IMessage message, double? radius = null) =>
        NetServer.Broadcast(message, c => {
            if (!c.InWorld || !players.TryGetValue(c.Id, out var ctx) || ctx.World != world
                || !ctx.World.Ecs.IsAlive(ctx.Entity))
                return false;
            var t = ctx.World.Ecs.Get<TransformEntityComponent>(ctx.Entity);
            // TODO: per-client radius
            double r = radius ?? (Streaming.ViewRadius + 1) * 16.0;
            double dx = t.X - x, dz = t.Z - z;
            return dx * dx + dz * dz <= r * r;
        });

    /// <summary>Broadcasts a chat component as a system chat message to an audience (default: in-world).</summary>
    public void BroadcastChatMessage(ChatComponent message, bool overlay = false, Func<NetClient, bool>? audience = null) {
        Sender.ReceiveMessage(message);
        BroadcastMessage(new SystemChatMessageS2C(message, overlay), audience);
    }

    /// <summary>Sets the player-list (tab) header and footer for an audience (default: every in-world client).
    /// Pass both - they travel in one packet; a null line is sent empty.</summary>
    public void SetTabListHeaderFooter(ChatComponent? header, ChatComponent? footer, Func<NetClient, bool>? audience = null) =>
        BroadcastMessage(new PlayerListHeaderFooterS2C(header ?? new TextComponent(""), footer ?? new TextComponent("")), audience);

    public CommandDispatcher CommandDispatcher { get; }

    /// <summary>The server console as a command/chat sender, owned here so every host shares one console
    /// identity. Issue console commands with <c>Console.ExecuteAsync(Commands, "/...")</c>.</summary>
    public ServerSender Sender { get; }

    /// <summary>Open container windows (chests) and their multiplayer sync.</summary>
    public ContainerManager Containers { get; }

    /// <summary>Domain event bus (player join/move/leave, ...). Built-in systems and hosts subscribe.</summary>
    public EventBus Events { get; }

    public ConcurrentDictionary<string, World> Worlds => context.Worlds;

    /// <summary>Creates the host-backed store for a world, for creating PERSISTENT worlds at runtime - pass
    /// the result to the <see cref="World"/> constructor in a <see cref="GetOrCreateWorld"/> factory; the
    /// world owns it from then on. Null when the host runs without persistence (the world is then in-memory).</summary>
    public IWorldStore? CreateWorldStore(string world) => context.WorldStoreFactory?.Invoke(world);
    public ChatComponent MOTD { get => context.MOTD; set => context.MOTD = value; }
    public int MaxPlayers => context.MaxPlayers;
    public double TicksPerSecond => context.TicksPerSecond;

    /// <summary>The number of ticks elapsed since <see cref="Start"/>.</summary>
    public long CurrentTick => Interlocked.Read(ref currentTick);

    /// <summary>Measured ticks-per-second over the last <paramref name="windowSeconds"/>, capped at the
    /// target. 0 until the loop has run a couple of ticks.</summary>
    public double MeasuredTps(double windowSeconds) => tps.Measure(Environment.TickCount64, windowSeconds);

    /// <summary>Players summed across every world.</summary>
    public int PlayerCount => Worlds.Values.Sum(w => w.PlayerCount);

    public readonly Scheduler Scheduler = new();

    public void Defer(Action work) => Scheduler.Defer(work);
    public Task DeferAsync(Action action) => Scheduler.DeferAsync(action);
    public Task<T> DeferAsync<T>(Func<T> func) => Scheduler.DeferAsync(func);
    public IDisposable Later(long delayTicks, Action callback) => Scheduler.Later(delayTicks, callback);
    public IDisposable Every(long periodTicks, Action callback) => Scheduler.Every(periodTicks, callback);
    public Task WaitForTicks(int ticks, CancellationToken ct = default) => Scheduler.Delay(ticks, ct);

    public Server(ServerContext ctx, INetServer netServer, IEntityStore? entityStore = null) {
        NetServer = netServer;
        Containers = new(this);
        Events = new(this);
        context = ctx;
        Sender = new(this);
        CommandDispatcher = new(this);
        if (context.TicksPerSecond <= 0)
            context.TicksPerSecond = 20.0;
        tps = new TpsTracker(context.TicksPerSecond);
        this.entityStore = entityStore ?? new InMemoryEntityStore();

        foreach (var world in Worlds.Values) {
            world.Events = Events;
            world.NextEntityId = NextEntityId; // TODO: Move NextEntityId handling into world
            world.LoadEntities(); // respawn dropped items etc. saved from a previous session (before any tick)
        }

        // Keep each world's spatial index current on any entity move (player or item).
        Events.Subscribe<EntityMoved>(OnEntityMoved);
        Events.Subscribe<PlayerJoined>(e => BroadcastChatMessage(new TextComponent($"{e.Client.Name} joined the game").SetColor(TextColor.Yellow)));
        Events.Subscribe<PlayerLeft>(e => {
            CommandDispatcher.Forget(e.Client.Id);
            BroadcastChatMessage(new TextComponent($"{e.Client.Name} left the game").SetColor(TextColor.Yellow));
        });

        // Chunk streaming registered before visibility so a joining client gets terrain before other spawns.
        Streaming.Register(Events);
        PlayerVisibility.Register(Events); // tab-list profiles only; entity spawn/despawn is the per-world EntityTrackerSystem
    }

    /// <summary>Cross-session entity persistence backend (in-memory unless the host supplied one).</summary>
    public IEntityStore EntityStore => entityStore;

    public World DefaultWorld => Worlds.Values.First();

    /// <summary>Starts the transport and launches the tick loop thread.</summary>
    public void Start() {
        if (running) return;
        running = true;

        NetServer.Start();

        // Background thread: the host owns process lifetime via WaitForShutdown, so a stopped server
        // never leaves a stray foreground thread alive.
        loopThread = new Thread(RunLoop) {
            Name = "SharpMinerals Tick Loop",
            IsBackground = true,
        };
        loopThread.Start();

        Log.LogInformation("Server started - {Tps} TPS, MOTD: \"{Motd}\"", TicksPerSecond, MOTD);
    }

    /// <summary>Signals the loop to stop, tears down the transport, flushes all saves, and releases
    /// <see cref="WaitForShutdown"/>. Idempotent - safe from Ctrl+C and <c>/server stop</c>.</summary>
    public void Stop() {
        if (Interlocked.Exchange(ref stopping, 1) == 1) return;
        running = false;
        NetServer.Stop();
        loopThread?.Join(TimeSpan.FromSeconds(5));
        // Transport + loop are down: apply any work queued just before shutdown, then flush everything.
        Scheduler.Run();
        SavePlayers();
        SaveWorlds();
        stopped.Set();
        Log.LogInformation("Server stopped");
    }

    /// <summary>Blocks until the server has fully stopped. A host calls this to run for the server's lifetime.</summary>
    public void WaitForShutdown() => stopped.Wait();

    /// <summary>Persists every world's modified chunks to its store (if any). Returns chunks written.</summary>
    public int SaveWorlds() {
        int total = 0;
        foreach (var world in Worlds.Values) {
            int saved = world.Save();
            total += saved;
            if (saved > 0)
                Log.LogInformation("Saved {Count} modified chunk(s) in '{World}'", saved, world.Name);
        }
        return total;
    }

    /// <summary>Snapshots every online player to the store. Returns the number saved.</summary>
    public int SavePlayers() {
        int count = 0;
        foreach (var (_, context) in players)
            if (context.World.Ecs.IsAlive(context.Entity)) {
                PersistPlayer(context.World, context.Entity);
                count++;
            }
        return count;
    }

    void PersistPlayer(World world, ArchEntity entity) {
        var ecs = world.Ecs;
        var info = ecs.Get<PlayerEntityComponent>(entity);
        // Generic entity encode: every persistent component on the live entity (placement, health, inventory)
        // goes through the shared component bag, keyed by the player's UUID.
        entityStore.Save(info.Uuid, EntityCodec.Encode(ecs, entity));
    }

    /// <summary>The game loop. PrecisionTimer paces it to a steady rate without busy-waiting.</summary>
    void RunLoop() {
        var timer = new PrecisionClock(1000.0 / TicksPerSecond);
        while (running) {
            try {
                Tick();
            } catch (Exception ex) {
                Log.LogError(ex, "Tick {Tick} failed", CurrentTick);
            }
            Interlocked.Increment(ref currentTick);
            tps.Record(Environment.TickCount64);
            timer.Wait();
        }
    }

    /// <summary>Autosave the world this often (~60s) once the single-writer drain makes it race-free.</summary>
    long AutosaveTicks => (long)(TicksPerSecond * 60);

    /// <summary>Sweep unviewable chunks this often (~10s).</summary>
    long EvictTicks => (long)(TicksPerSecond * 10);

    /// <summary>Extra ring of chunks kept loaded beyond the streamed view, to avoid reload churn.</summary>
    const int EvictMargin = 2;

    public void Tick() {
        // Hold the ECS gate for the whole tick so a network-thread join/leave can't race the drain,
        // the parallel entity systems, or the autosave - all of which touch the ECS.
        lock (ecsGate) {
            Scheduler.Run();

            Worlds.Values.AsParallel().ForAll(world => {
                foreach(var system in world.NetworkSystems) {
                    system.Announce(this);
                }

                world.Tick();

                foreach(var system in world.NetworkSystems) {
                    system.Flush(this);
                }
            });

            Scheduler.Run();

            if(currentTick % (long)TicksPerSecond == 0)
                NetServer.Broadcast(new KeepAliveS2C(currentTick), c => c.InWorld);

            if (currentTick > 0 && currentTick % AutosaveTicks == 0) {
                SaveWorlds();
                SavePlayers();
            }

            if (currentTick > 0 && currentTick % EvictTicks == 0)
                EvictChunks();
        }
    }

    /// <summary>Drops chunks no online player can see (saving dirty ones first) across every world.</summary>
    public void EvictChunks() {
        var centersByWorld = new Dictionary<World, List<(long X, long Z)>>();
        foreach (var (_, context) in players) {
            if (!context.World.Ecs.IsAlive(context.Entity)) continue;
            var view = context.World.Ecs.Get<ChunkViewEntityComponent>(context.Entity);
            if (!view.Initialized) continue;
            if (!centersByWorld.TryGetValue(context.World, out var centers))
                centersByWorld[context.World] = centers = new();
            centers.Add((view.CenterX, view.CenterZ));
        }

        int keepRadius = Streaming.ViewRadius + EvictMargin;
        foreach (var world in Worlds.Values) {
            centersByWorld.TryGetValue(world, out var centers);
            int evicted = world.EvictChunks(centers, keepRadius);
            if (evicted > 0)
                Log.LogDebug("Evicted {Count} chunk(s) from '{World}'", evicted, world.Name);
        }
    }

    /// <summary>Every connected player (client id -> world + entity).</summary>
    public IEnumerable<KeyValuePair<ulong, PlayerContext>> Players => players;

    public readonly string Version = typeof(Server).Assembly.GetName().Version?.ToString(3) ?? "?";

    /// <summary>Spawns a player for a freshly logged-in client and returns its network entity id.</summary>
    public int AddPlayer(NetClient client, string name, Guid uuid) {
        var world = DefaultWorld;
        int entityId = NextEntityId();
        // Restore a previous session's entity state if any.
        byte[]? saved = entityStore.TryLoad(uuid, out var blob) ? blob : null;
        ArchEntity entity;
        // Entity creation is a structural change - serialize it with the tick so it can't run while a
        // world-tick query iterates archetypes on another thread.
        lock (ecsGate) {
            entity = world.SpawnPlayer(client.Id, name, uuid, entityId, saved);
            // Inject the player's chat sender connection (chat delivery + command-source identity).
            if (world.Ecs.Has<SenderEntityComponent>(entity))
                world.Ecs.Get<SenderEntityComponent>(entity).Client = client;
        }
        players[client.Id] = new PlayerContext(this, world, entity, client);
        Log.LogInformation("{Name} (#{Client}, eid {Eid}) joined{Restored} - {Online} online",
            name, client.Id, entityId, saved is null ? "" : " (restored)", PlayerCount);
        return entityId;
    }

    /// <summary>Gets a world by name, creating one via <paramref name="factory"/> (wired to the bus and
    /// ticked with the others) if it doesn't exist.</summary>
    public World GetOrCreateWorld(string name, Func<string, Server, World> factory) =>
        Worlds.GetOrAdd(name, n => {
            var world = factory.Invoke(n, this);
            world.Events = Events;             // wire a dynamically-created world the same as the startup ones
            world.NextEntityId = NextEntityId; // ...incl. the loose-entity id allocator (else items/falling blocks get id 0)
            world.LoadEntities();              // respawn saved loose entities for a store-backed world (no-op otherwise);
                                               // safe pre-publication: the tick loop can't see the world until GetOrAdd returns
            return world;
        });

    /// <summary>Unloads <paramref name="target"/>, freeing its ECS + chunks. Refuses the default world or
    /// one that still has players. Runs under the ECS gate so teardown can't race a tick. Returns whether
    /// it unloaded.</summary>
    public bool UnloadWorld(World target) {
        if (ReferenceEquals(target, DefaultWorld))
            return false;
        lock (ecsGate) {
            if (target.PlayerCount > 0)
                return false; // never pull a world out from under a player
            if (!Worlds.TryRemove(new KeyValuePair<string, World>(target.Name, target)))
                return false; // already gone, or a different instance under that name
            target.Save(); // flush dirty chunks + loose entities first (no-op without a store)
            target.Unload();
        }
        Log.LogInformation("Unloaded world '{World}'", target.Name);
        return true;
    }

#if TEST_HARNESS
    /// <summary>Raised when a test client replies on the <c>sharptester:cmd</c> control channel: (clientId,
    /// reply text). Fired on a network receive thread.</summary>
    public event Action<ulong, string>? TestClientReply;
    internal void RaiseTestClientReply(ulong clientId, string reply) => TestClientReply?.Invoke(clientId, reply);
#endif

    /// <summary>Moves a connected player to <paramref name="target"/> WITHOUT a reconnect: carries the entity
    /// (position, health, inventory; same network id), respawns the client, and re-streams chunks. The respawn
    /// uses the target's unique world name as the dimension key, which forces the client to drop the old
    /// world's entities/chunks; the dimension <em>type</em> stays <c>minecraft:overworld</c>.</summary>
    public void SwitchWorld(ulong clientId, World target) {
        if (!players.TryGetValue(clientId, out var ctx) || !ctx.World.Ecs.IsAlive(ctx.Entity))
            return;
        if (ReferenceEquals(target, ctx.World))
            return; // already there
        var client = ctx.Client;
        var old = ctx.World;
        var info = old.Ecs.Get<PlayerEntityComponent>(ctx.Entity);

        // The old world's entity tracker despawns this player for its viewers once the entity leaves it (below);
        // the target world's tracker spawns it there. The tab-list entry stays (the player is still online).
        // Resolve the target's spawn before taking the gate - the surface-scan path may generate a chunk.
        var spawn = target.GetSpawn();
        PlayerContext moved;
        lock (ecsGate) {
            if (!old.Ecs.IsAlive(ctx.Entity)) return;
            // Carry the persistent state across (same encode as a disk save); reuse the same network entity id.
            var blob = EntityCodec.Encode(old.Ecs, ctx.Entity);
            old.DestroyEntity(ctx.Entity);
            var entity = target.SpawnPlayer(clientId, info.Name, info.Uuid, info.NetId, blob);
            if (target.Ecs.Has<SenderEntityComponent>(entity))
                target.Ecs.Get<SenderEntityComponent>(entity).Client = client;
            // The restored blob carried OLD-world coordinates - a switch enters through the target's spawn.
            target.Ecs.Get<TransformEntityComponent>(entity) = spawn;
            target.Ecs.Get<NetTransformEntityComponent>(entity) = new NetTransformEntityComponent {
                Position = spawn.Position, Yaw = spawn.Yaw, Pitch = spawn.Pitch
            };
            target.Entities.Update(entity, spawn.X, spawn.Y, spawn.Z);
            // Adopt the target's imposed mode; without one, keep the mode carried from the old world
            // (SpawnPlayer rebuilt the session component, which resets it).
            target.Ecs.Get<PlayerEntityComponent>(entity).GameMode = target.DefaultGameMode ?? info.GameMode;
            moved = new PlayerContext(this, target, entity, client);
            players[clientId] = moved;
        }

        // Reload the client into the target world (WorldName = target's unique key forces the teardown).
        byte modeId = target.Ecs.Get<PlayerEntityComponent>(moved.Entity).GameMode.IntoId();
        client.Send(new RespawnS2C("minecraft:overworld", target.Name, HashedSeed: 0, GameMode: modeId, IsFlat: true));
        client.Send(new SetDefaultSpawnPositionS2C(
            new Vector3i((int)System.Math.Floor(spawn.X), (int)System.Math.Floor(spawn.Y), (int)System.Math.Floor(spawn.Z)), spawn.Yaw));
        // Send the spawn column synchronously (like the join flow) so the client lands on loaded terrain in the new
        // world immediately; the surrounding columns then stream in from the background loader.
        Streaming.StreamSpawnColumn(moved);
        Streaming.StreamInitial(moved); // fresh ChunkView => streams the new world's columns
        var t = target.Ecs.Get<TransformEntityComponent>(moved.Entity);
        client.Send(new SynchronizePlayerPositionS2C(t.X, t.Y, t.Z, t.Yaw, t.Pitch, BeginTeleport(clientId)));
        client.Send(new SetHealthS2C(target.Ecs.Get<HealthEntityComponent>(moved.Entity).Current, 20, 5f));
        // World changed - invalidate cached parses so world/dimension-gated .Requires re-evaluate.
        CommandDispatcher.Invalidate(clientId);
        // ...and forget the cached inventory window: it captured the OLD entity's inventory, but the switch
        // re-spawned the player with a fresh one, so a stale window would sync/apply against the wrong inventory.
        Containers.OnPlayerWorldChanged(clientId);
        Log.LogInformation("{Name} switched to world '{World}'", info.Name, target.Name);
    }

    // Re-files a moved entity in its world's spatial index (concurrent: runs on tick + network threads).
    static void OnEntityMoved(EntityMoved e) {
        if (!e.World.Ecs.IsAlive(e.Entity)) return; // despawned between move and (deferred) handling
        var t = e.World.Ecs.Get<TransformEntityComponent>(e.Entity);
        e.World.Entities.Update(e.Entity, t.X, t.Y, t.Z);
    }

    // -- Teleports ------------------------------------------------------------
    /// <summary>Registers a pending teleport: the client's position updates are ignored until it confirms
    /// this id. Returns the id to send in the SynchronizePlayerPosition packet.</summary>
    public int BeginTeleport(ulong clientId) {
        int id = Interlocked.Increment(ref nextTeleportId);
        pendingTeleports[clientId] = id;
        return id;
    }

    /// <summary>True while a client has an unconfirmed teleport (its position updates should be ignored).</summary>
    public bool IsTeleportPending(ulong clientId) => pendingTeleports.ContainsKey(clientId);

    /// <summary>Clears the pending teleport once the client confirms the matching id.</summary>
    public void ConfirmTeleport(ulong clientId, int teleportId) {
        if (pendingTeleports.TryGetValue(clientId, out var pending) && pending == teleportId)
            pendingTeleports.TryRemove(new KeyValuePair<ulong, int>(clientId, teleportId));
    }

    /// <summary>Teleports a player to a position: streams terrain around the destination, shows the move to
    /// others, sends the position sync, and ignores the client's stale positions until it confirms.</summary>
    public void TeleportPlayer(ulong clientId, double x, double y, double z, float yaw, float pitch) {
        if (!players.TryGetValue(clientId, out var context) || !context.World.Ecs.IsAlive(context.Entity))
            return;
        if (!NetServer.TryGetClient(clientId, out var client))
            return;

        int teleportId = BeginTeleport(clientId); // ignore the client's positions until it confirms

        ref var t = ref context.World.Ecs.Get<TransformEntityComponent>(context.Entity);
        t.X = x; t.Y = y; t.Z = z; t.Yaw = yaw; t.Pitch = pitch;

        // Stream terrain around the destination now and re-file the spatial index; the per-tick movement system
        // shows the move to other players. Then teleport the client.
        Streaming.Restream(context);
        Events.Publish(new EntityMoved(context.World, context.Entity));
        client.Send(new SynchronizePlayerPositionS2C(x, y, z, yaw, pitch, teleportId));
    }

    /// <summary>Despawns a disconnected client's player entity and tells others it left.</summary>
    public void RemovePlayer(ulong clientId) {
        Containers.OnLeave(clientId);
        pendingTeleports.TryRemove(clientId, out _);
        if (!players.TryRemove(clientId, out var context))
            return;
        // Despawn under the tick gate: destroying + reading the entity must not race the world-tick queries.
        lock (ecsGate) {
            if (context.World.Ecs.IsAlive(context.Entity)) {
                // Persist so a reconnect (same UUID) restores it.
                PersistPlayer(context.World, context.Entity);
                Events.Publish(new PlayerLeft(context));
                context.World.DestroyEntity(context.Entity);
            }
        }
    }

    public bool TryGetPlayer(ulong clientId, [System.Diagnostics.CodeAnalysis.MaybeNullWhen(false)] out PlayerContext context) =>
        players.TryGetValue(clientId, out context);
}
