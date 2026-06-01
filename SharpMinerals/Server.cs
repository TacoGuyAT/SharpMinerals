using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using SharpMinerals.Entities;
using SharpMinerals.Entities.Components;
using SharpMinerals.Events;
using SharpMinerals.Events.Contexts;
using SharpMinerals.Level;
using SharpMinerals.Math;
using SharpMinerals.Network;
using SharpMinerals.Network.Messages;
using SharpMinerals.Persistence;
using PrecisionClock = PrecisionTimer.PrecisionTimer;
using ArchEntity = Arch.Core.Entity;
using SharpMinerals.Chat;
using SharpMinerals.Network.Containers;
using SharpMinerals.Commands;

namespace SharpMinerals;

/// <summary>
/// The top-level server. Drives a fixed-rate game loop on its own thread using
/// <see cref="PrecisionClock"/> and ticks every world each tick. The transport
/// (<see cref="NetServer"/>) runs its own threads and feeds decoded messages into
/// server logic.
/// </summary>
public class Server : ITickable {
    static readonly ILogger Log = Logging.For("Server");

    /// <summary>True while the game loop is running.</summary>
    public bool IsRunning => running;

    ServerContext context;
    readonly IPlayerStore playerStore;
    volatile bool running;
    int stopping; // 0/1 guard so Stop() runs its teardown exactly once
    readonly ManualResetEventSlim stopped = new(false);
    Thread? loopThread;
    long currentTick;
    int nextEntityId;
    int nextTeleportId;

    // Clients with an unconfirmed teleport — their position updates are ignored until they send
    // Confirm Teleportation with the matching id (else a teleport-far player bounces back).
    readonly ConcurrentDictionary<ulong, int> pendingTeleports = new();

    /// <summary>Allocates a fresh network entity id for spawned entities.</summary>
    public int NextEntityId() => Interlocked.Increment(ref nextEntityId);

    /// <summary>Maps a connected client to the world + entity that represents its player.</summary>
    readonly ConcurrentDictionary<ulong, PlayerContext> players = new();

    /// <summary>Serializes ECS structural changes (player spawn/despawn on network threads) against the
    /// tick's entity systems, so a join/leave can't race the simulation's queries. Held only briefly.</summary>
    readonly object ecsGate = new();

    public INetServer NetServer => context.NetServer;

    /// <summary>
    /// The general broadcast entry point: sends a message to an audience of clients — by default every
    /// in-world client. Callers pass the message (and optionally narrow the audience) instead of
    /// repeating the connection-state predicate; the transport encodes once per protocol version and
    /// silently drops the message for any client whose protocol can't encode it.
    /// </summary>
    public void BroadcastMessage(IMessage message, Func<NetClient, bool>? audience = null) =>
        NetServer.Broadcast(message, audience ?? (static c => c.InWorld));

    /// <summary>Broadcasts a chat component as a system chat message to an audience (default: in-world).
    /// The chat-flavoured wrapper over <see cref="BroadcastMessage"/> so callers don't build the packet.</summary>
    public void BroadcastChatMessage(ChatComponent message, bool overlay = false, Func<NetClient, bool>? audience = null) {
        Sender.ReceiveMessage(message);
        BroadcastMessage(new SystemChatMessageS2C(message, overlay), audience);
    }

    /// <summary>Sets the player-list (tab) header and footer for an audience of clients (default: every
    /// in-world client). The header and footer travel in one packet, so pass both — a null line is sent
    /// empty. Narrow the audience with <paramref name="audience"/> (e.g. just the joining player).</summary>
    public void SetTabListHeaderFooter(ChatComponent? header, ChatComponent? footer, Func<NetClient, bool>? audience = null) =>
        BroadcastMessage(new PlayerListHeaderFooterS2C(header ?? new TextComponent(""), footer ?? new TextComponent("")), audience);

    /// <summary>The command dispatcher, so any <see cref="Commands.ISender"/> can issue commands.
    /// Assigning it injects this server into the dispatcher (so it and its commands need no static access).</summary>
    public CommandDispatcher CommandDispatcher { get => commandDispatcher; }
    readonly CommandDispatcher commandDispatcher = new();

    /// <summary>The server console as a command/chat sender, owned here so every host shares one console
    /// identity: the CLI wires its stdin/stdout to it, and an in-process test drives commands as it and reads
    /// the replies. Issue console commands with <c>Console.ExecuteAsync(Commands, "/…")</c>.</summary>
    public ServerSender Sender { get; }

    /// <summary>Open container windows (chests) and their multiplayer sync.</summary>
    public ContainerManager Containers { get; } = new();

    /// <summary>Domain event bus (player join/move/leave, …). Built-in systems and hosts subscribe.</summary>
    public EventBus Events { get; } = new();

    public ConcurrentDictionary<string, World> Worlds => context.Worlds;
    public string MOTD { get => context.MOTD; set => context.MOTD = value; }
    public int MaxPlayers => context.MaxPlayers;
    public double TicksPerSecond => context.TicksPerSecond;

    /// <summary>The number of ticks elapsed since <see cref="Start"/>.</summary>
    public long CurrentTick => Interlocked.Read(ref currentTick);

    /// <summary>Players summed across every world.</summary>
    public int PlayerCount => Worlds.Values.Sum(w => w.PlayerCount);

    public Server(ServerContext ctx) {
        context = ctx;
        Sender = new(this);
        // Inject ourselves so the dispatcher and its commands reach the server through the command source
        // (CommandContext.Server) rather than a global static.
        commandDispatcher.Server = this;
        if (context.TicksPerSecond <= 0)
            context.TicksPerSecond = 20.0;
        playerStore = ctx.PlayerStore ?? new InMemoryPlayerStore();

        // Let each owned world publish its simulation events (entity moves, …) onto our bus.
        foreach (var world in Worlds.Values)
            world.Events = Events;

        // Keep each world's spatial index current: any entity move (item OR player — PlayerMoved is an
        // EntityMoved) re-files the entity, re-bucketing only when it crosses a chunk boundary.
        Events.Subscribe<EntityMoved>(OnEntityMoved);
        Events.Subscribe<PlayerJoined>((e) => BroadcastChatMessage(new TextComponent($"{e.Context.Player.Name} joined the game").SetColor(TextColor.Yellow)));
        Events.Subscribe<PlayerLeft>((e) => BroadcastChatMessage(new TextComponent($"{e.Context.Player.Name} left the game").SetColor(TextColor.Yellow)));
        // Free the disconnected player's command-parse cache state (their cached parses lapse via the TTL).
        Events.Subscribe<PlayerLeft>((e) => commandDispatcher.Forget(e.Context.Client.Id));

        // Wire the built-in systems to the event bus. Chunk streaming before visibility so a
        // joining client gets terrain before other players' spawns.
        ChunkStreamer.Register(Events);
        PlayerVisibility.Register(Events);
        // Broadcast the client effects of the world simulation's deferred pickup/landing events.
        Network.Handlers.EntityNetworking.RegisterHandlers(this);
    }

    /// <summary>Cross-session player persistence backend (in-memory unless the host supplied one).</summary>
    public IPlayerStore PlayerStore => playerStore;

    public World DefaultWorld => Worlds.Values.First();

    /// <summary>Coerce an arbitrary world name into the characters a Minecraft resource-location path allows.</summary>
    static string SanitizeId(string name) {
        var chars = name.ToLowerInvariant().Select(c =>
            c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-' or '_' or '/' ? c : '_');
        return new([.. chars]);
    }

    /// <summary>Starts the transport and launches the tick loop thread.</summary>
    public void Start() {
        if (running) return;
        running = true;

        NetServer.Start();

        // Background: the host owns the process lifetime by blocking on WaitForShutdown, so a
        // stopped server never leaves a stray foreground thread keeping the app alive.
        loopThread = new Thread(RunLoop) {
            Name = "SharpMinerals Tick Loop",
            IsBackground = true,
        };
        loopThread.Start();

        Log.LogInformation("Server started — {Tps} TPS, MOTD: \"{Motd}\"", TicksPerSecond, MOTD);
    }

    /// <summary>
    /// Signals the loop to stop, tears down the transport, flushes all saves, and releases
    /// <see cref="WaitForShutdown"/>. Idempotent — safe to call from Ctrl+C and <c>/server stop</c>.
    /// </summary>
    public void Stop() {
        if (Interlocked.Exchange(ref stopping, 1) == 1) return; // already stopping
        running = false;
        NetServer.Stop();
        loopThread?.Join(TimeSpan.FromSeconds(5));
        // Transport + loop are down (single-threaded now): apply any work queued just before
        // shutdown (e.g. a final /save or a last block edit), then flush everything.
        Events.DrainDeferred();
        SavePlayers();
        SaveWorlds();
        stopped.Set();
        Log.LogInformation("Server stopped");
    }

    /// <summary>
    /// Blocks until the server has fully stopped (via Ctrl+C, <c>/server stop</c>, or a direct
    /// <see cref="Stop"/>). A host calls this to run for the server's lifetime, then clean up + exit.
    /// </summary>
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
        var info = ecs.Get<NetPlayerEntityComponent>(entity);
        playerStore.Save(info.Uuid, new PlayerState(
            ecs.Get<TransformEntityComponent>(entity),
            ecs.Get<HealthEntityComponent>(entity),
            ecs.Get<InventoryEntityComponent>(entity)));
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

    /// <summary>Autosave the world this often (~60s) once the single-writer drain makes it race-free.</summary>
    long AutosaveTicks => (long)(TicksPerSecond * 60);

    /// <summary>Sweep unviewable chunks this often (~10s).</summary>
    long EvictTicks => (long)(TicksPerSecond * 10);

    /// <summary>Extra ring of chunks kept loaded beyond the streamed view, to avoid reload churn.</summary>
    const int EvictMargin = 2;

    public void Tick() {
        // Hold the ECS gate for the whole tick: the drain (block/drop creation), the parallel entity
        // systems (which now own pickup despawns + falling-block landing), and the autosave (reads player
        // entities) all touch the ECS, and a network-thread join/leave must not run concurrently with any.
        lock (ecsGate) {
            // Single-writer phase: apply queued mutations/events (block break/place, container edits)
            // on THIS thread first, so the rest of the tick — and autosave — sees a consistent world
            // with no concurrent chunk edits from the network threads.
            Events.DrainDeferred();

            // Announce newly-spawned entities (drops, falling blocks) BEFORE physics, so the client gets
            // their un-decayed spawn position + velocity and mirrors the motion the server is about to run.
            Network.Handlers.EntityNetworking.AnnounceNew(this);

            // Worlds are independent, so they can tick in parallel. The per-world systems now own the
            // simulation — gravity/collision, item pickup, and falling-block landing — and publish their
            // client effects as DEFERRED events, broadcast on this thread when the bus next drains.
            Worlds.Values.AsParallel().ForAll(w => w.Tick());

            // Keep connections alive once a second: ONE generic KeepAliveS2C, mapped per protocol at encode
            // (modern long id / legacy 0x00 int). Reaches modern Play clients + in-world legacy clients.
            if (currentTick % (long)TicksPerSecond == 0)
                NetServer.Broadcast(new KeepAliveS2C(currentTick), c => c.InWorld);

            // Periodic autosave — safe now that all world + player mutations are confined to the drain
            // phase above (this thread). Serializes on this thread; the async stores flush off-thread.
            if (currentTick > 0 && currentTick % AutosaveTicks == 0) {
                SaveWorlds();
                SavePlayers();
            }

            // Periodic chunk eviction — drop chunks no player can see (saving dirty ones), bounding
            // memory/disk. Safe on this thread for the same single-writer reason as autosave.
            if (currentTick > 0 && currentTick % EvictTicks == 0)
                EvictChunks();
        }
    }

    /// <summary>Drops chunks no online player can see (saving dirty ones first) across every world.</summary>
    public void EvictChunks() {
        // Gather each online player's view-centre column, grouped by world.
        var centersByWorld = new Dictionary<World, List<(long X, long Z)>>();
        foreach (var (_, context) in players) {
            if (!context.World.Ecs.IsAlive(context.Entity)) continue;
            var view = context.World.Ecs.Get<ChunkViewEntityComponent>(context.Entity);
            if (!view.Initialized) continue;
            if (!centersByWorld.TryGetValue(context.World, out var centers))
                centersByWorld[context.World] = centers = new();
            centers.Add((view.CenterX, view.CenterZ));
        }

        int keepRadius = ChunkStreamer.ViewRadius + EvictMargin;
        foreach (var world in Worlds.Values) {
            centersByWorld.TryGetValue(world, out var centers);
            int evicted = world.EvictChunks(centers, keepRadius);
            if (evicted > 0)
                Log.LogDebug("Evicted {Count} chunk(s) from '{World}'", evicted, world.Name);
        }
    }

    /// <summary>Every connected player (client id → world + entity).</summary>
    public IEnumerable<KeyValuePair<ulong, PlayerContext>> Players => players;

    /// <summary>Spawns a player for a freshly logged-in client and returns its network entity id.</summary>
    public int AddPlayer(NetClient client, string name, Guid uuid) {
        var world = DefaultWorld;
        int entityId = NextEntityId();
        // Restore a previous session's entity state (position/rotation/health/inventory) if any.
        PlayerState? saved = playerStore.TryLoad(uuid, out var state) ? state : null;
        ArchEntity entity;
        // The ECS entity creation is a structural change — serialize it with the tick (see Tick) so it
        // can't run while a world-tick query is iterating archetypes on another thread.
        lock (ecsGate) {
            entity = world.SpawnPlayer(client.Id, name, uuid, entityId, saved);
            // Give the player's chat sender its backing connection (chat delivery + command-source identity)
            // — the component reaches the client through this injected reference, no global static.
            if (world.Ecs.Has<SenderEntityComponent>(entity))
                world.Ecs.Get<SenderEntityComponent>(entity).Client = client;
        }
        players[client.Id] = new PlayerContext(this, world, entity, client);
        Log.LogInformation("{Name} (#{Client}, eid {Eid}) joined{Restored} — {Online} online",
            name, client.Id, entityId, saved is null ? "" : " (restored)", PlayerCount);
        return entityId;
    }

    /// <summary>Gets a world by name, creating a fresh in-memory superflat one (wired to the event bus and
    /// ticked with the others) if it doesn't exist — the basis for per-test world isolation on a live
    /// connection.</summary>
    public World GetOrCreateWorld(string name, Func<string, Server, World> factory) =>
        Worlds.GetOrAdd(name, n => factory.Invoke(n, this));

    /// <summary>Unloads <paramref name="target"/>: removes it from the world set (so the tick loop stops
    /// ticking it) and frees its ECS + chunks. Refuses the default world or one that still has players.
    /// Runs under the ECS gate, which the tick also holds — so the teardown can't race a tick mid-flight.
    /// Returns whether it unloaded.</summary>
    public bool UnloadWorld(World target) {
        if (ReferenceEquals(target, DefaultWorld))
            return false; // the lobby/default world stays loaded for the session
        lock (ecsGate) {
            if (target.PlayerCount > 0)
                return false; // never pull a world out from under a player
            if (!Worlds.TryRemove(new KeyValuePair<string, World>(target.Name, target)))
                return false; // already gone, or a different instance under that name
            target.Unload();
        }
        Log.LogInformation("Unloaded world '{World}'", target.Name);
        return true;
    }

#if TEST_HARNESS
    /// <summary>Raised when a test client replies on the <c>sharptester:cmd</c> control channel: (clientId,
    /// reply text). The in-process harness subscribes here to read a command's result back over the channel —
    /// it sends the command itself through the existing <c>/test</c> command. Fired on a network receive thread.</summary>
    public event Action<ulong, string>? TestClientReply;
    internal void RaiseTestClientReply(ulong clientId, string reply) => TestClientReply?.Invoke(clientId, reply);
#endif

    /// <summary>Moves a connected player to <paramref name="target"/> WITHOUT a reconnect: carries the entity
    /// (position, health, inventory; same network id) into it, Respawns the client, re-streams the new
    /// world's chunks, and re-positions the player. The Respawn carries the target world's UNIQUE dimension key
    /// (see <see cref="DimensionId"/>), which is what forces the client to drop the old world's entities/chunks
    /// and fully reload — the dimension <em>type</em> stays <c>minecraft:overworld</c> (all worlds are flat).</summary>
    public void SwitchWorld(ulong clientId, World target) {
        if (!players.TryGetValue(clientId, out var ctx) || !ctx.World.Ecs.IsAlive(ctx.Entity))
            return;
        if (ReferenceEquals(target, ctx.World))
            return; // already there
        var client = ctx.Client;
        var old = ctx.World;
        var info = old.Ecs.Get<NetPlayerEntityComponent>(ctx.Entity);

        // Despawn the entity for anyone who could still see it (the player stays online — no tab-list change).
        NetServer.Broadcast(new RemoveEntitiesS2C(new[] { info.EntityId }), c => c.InWorld && c.Id != clientId);

        PlayerContext moved;
        lock (ecsGate) {
            if (!old.Ecs.IsAlive(ctx.Entity)) return;
            // Carry the persistent state across; reuse the same network entity id.
            var state = new PlayerState(
                old.Ecs.Get<TransformEntityComponent>(ctx.Entity),
                old.Ecs.Get<HealthEntityComponent>(ctx.Entity),
                old.Ecs.Get<InventoryEntityComponent>(ctx.Entity));
            old.DestroyEntity(ctx.Entity);
            var entity = target.SpawnPlayer(clientId, info.Name, info.Uuid, info.EntityId, state);
            if (target.Ecs.Has<SenderEntityComponent>(entity))
                target.Ecs.Get<SenderEntityComponent>(entity).Client = client;
            moved = new PlayerContext(this, target, entity, client);
            players[clientId] = moved;
        }

        // Reload the client into the target world. WorldName is the target's unique key (≠ the old world's),
        // which is what makes the client tear down the old entities/chunks; the type stays overworld.
        client.Send(new RespawnS2C("minecraft:overworld", target.Name, HashedSeed: 0, GameMode: 1, IsFlat: true));
        client.Send(new SetDefaultSpawnPositionS2C(new Vector3i(0, FlatChunkGenerator.SurfaceY, 0), 0f));
        ChunkStreamer.StreamInitial(moved); // fresh ChunkView ⇒ streams the new world's columns
        var t = target.Ecs.Get<TransformEntityComponent>(moved.Entity);
        client.Send(new SynchronizePlayerPositionS2C(t.X, t.Y, t.Z, t.Yaw, t.Pitch, BeginTeleport(clientId)));
        client.Send(new SetHealthS2C(target.Ecs.Get<HealthEntityComponent>(moved.Entity).Current, 20, 5f));
        // The player's world changed — invalidate their cached parses so any world/dimension-gated .Requires
        // re-evaluates against the new world on next command.
        commandDispatcher.Invalidate(clientId);
        Log.LogInformation("{Name} switched to world '{World}'", info.Name, target.Name);
    }

    // Re-files a moved entity in its world's spatial index. Runs for items on the tick thread
    // (deferred drain) and for players on the network thread; the index is concurrent.
    static void OnEntityMoved(EntityMoved e) {
        if (!e.World.Ecs.IsAlive(e.Entity)) return; // despawned between move and (deferred) handling
        var t = e.World.Ecs.Get<TransformEntityComponent>(e.Entity);
        e.World.Entities.Update(e.Entity, t.X, t.Y, t.Z);
    }

    // ── Teleports ────────────────────────────────────────────────────────────
    /// <summary>
    /// Registers a teleport (a fresh id, marked pending): the client's position updates are ignored
    /// until it confirms this id. Returns the id to send in the SynchronizePlayerPosition packet.
    /// </summary>
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

    /// <summary>
    /// Teleports a player to a position: streams terrain around the destination and shows the move
    /// to other players, then sends the position sync and ignores the client's stale positions
    /// until it confirms.
    /// </summary>
    public void TeleportPlayer(ulong clientId, double x, double y, double z, float yaw, float pitch) {
        if (!players.TryGetValue(clientId, out var context) || !context.World.Ecs.IsAlive(context.Entity))
            return;
        if (!NetServer.TryGetClient(clientId, out var client))
            return;

        int teleportId = BeginTeleport(clientId); // ignore the client's positions until it confirms

        ref var t = ref context.World.Ecs.Get<TransformEntityComponent>(context.Entity);
        t.X = x; t.Y = y; t.Z = z; t.Yaw = yaw; t.Pitch = pitch;

        // Stream chunks around the destination + broadcast the move, then teleport the client.
        Events.Publish(new PlayerMoved(context));
        client.Send(new SynchronizePlayerPositionS2C(x, y, z, yaw, pitch, teleportId));
    }

    /// <summary>Despawns a disconnected client's player entity and tells others it left.</summary>
    public void RemovePlayer(ulong clientId) {
        Containers.OnLeave(clientId);
        pendingTeleports.TryRemove(clientId, out _);
        if (!players.TryRemove(clientId, out var context))
            return;
        // Despawn under the same gate as the tick — destroying the entity (and reading it to persist)
        // must not race the world-tick queries.
        lock (ecsGate) {
            if (context.World.Ecs.IsAlive(context.Entity)) {
                // Persist the entity's state so a reconnect (same UUID) restores it.
                PersistPlayer(context.World, context.Entity);
                Events.Publish(new PlayerLeft(context));
                context.World.DestroyEntity(context.Entity);
            }
        }
    }

    /// <summary>Looks up the world + entity backing a connected client.</summary>
    public bool TryGetPlayer(ulong clientId, [System.Diagnostics.CodeAnalysis.MaybeNullWhen(false)] out PlayerContext context) =>
        players.TryGetValue(clientId, out context);
}
