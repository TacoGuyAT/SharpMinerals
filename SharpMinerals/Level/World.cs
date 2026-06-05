using System.Collections.Concurrent;
using Arch.Core;
using SharpMinerals.Blocks;
using SharpMinerals.Components;
using SharpMinerals.Entities;
using SharpMinerals.Entities.Components;
using SharpMinerals.Entities.Descriptors;
using SharpMinerals.Events;
using SharpMinerals.Events.Contexts;
using SharpMinerals.Items;
using SharpMinerals.Math;
using SharpMinerals.Network.Buffers;
using SharpMinerals.Persistence;
using ArchWorld = Arch.Core.World;
using ArchEntity = Arch.Core.Entity;

namespace SharpMinerals.Level;

/// <summary>A single dimension: owns an Arch ECS <see cref="ArchWorld"/> and a lazily-generated grid of
/// cuboid chunks. <see cref="Tick"/> runs the per-tick systems; block reads/writes go through the chunk grid.</summary>
public class World : ITickable {
    static readonly QueryDescription PlayerQuery = new QueryDescription().WithAll<NetPlayerEntityComponent>();
    static readonly QueryDescription TypedEntityQuery = new QueryDescription().WithAll<TypeEntityDescriptor>();
    const byte EntitiesVersion = 1;

    readonly IChunkGenerator chunkGenerator;
    readonly IWorldStore? store;
    readonly ConcurrentDictionary<Vector3i, Chunk> loadedChunks = new();

    // The per-tick entity systems, run in registration order each Tick.
    readonly List<ITickable> systems;

    /// <summary>The world's per-tick systems. The server scans these for <c>INetworkSystem</c> to drive each
    /// one's client projection, so a world that omits a system also sends none of its packets.</summary>
    public IReadOnlyList<ITickable> Systems => systems;

    public string Name { get; }

    public ArchWorld Ecs { get; } = ArchWorld.Create();

    public bool IsActive { get; set; } = true;

    /// <summary>The domain event bus the simulation publishes to (null in standalone use, e.g. tests).
    /// Systems publish deferred, since worlds tick on parallel threads.</summary>
    public EventBus? Events { get; set; }

    public World(string name, IWorldStore? store = null) : this(name, IChunkGenerator.Default, store) { }

    public World(string name, IChunkGenerator chunkGenerator, IWorldStore? store = null) {
        Name = name;
        this.chunkGenerator = chunkGenerator;
        this.store = store;
        Entities = new SpatialIndex(this);
        systems = new List<ITickable> {
            new Systems.ItemLifecycleSystem(this),
            new Systems.EntityPhysicsSystem(this),      // gravity + terrain collision; writes block-collision feedback
            new Systems.FallingBlockSystem(this),       // lands falling blocks using that ground-contact feedback
            new Systems.CollisionFeedbackSystem(this),  // entity-vs-entity overlap, on settled positions
            new Systems.ItemPickupSystem(this),         // collects overlapped drops into player inventories
            new Systems.EquipmentVisibilitySystem(this),// diffs each player's equipment -> others (post-tick)
            new Systems.PlayerMovementSystem(this),     // relays each player's movement -> others (post-tick)
            new Systems.ChunkStreamingSystem(this),     // streams columns as a player crosses chunks (post-tick)
        };
    }

    // -- Chunks --------------------------------------------------------------
    /// <summary>Gets a chunk by chunk coordinate, loading it from the store (or generating) on first access.</summary>
    public Chunk GetChunk(Vector3i chunkPosition) =>
        loadedChunks.GetOrAdd(chunkPosition, LoadOrGenerate);

    Chunk LoadOrGenerate(Vector3i pos) {
        var chunk = store is not null && store.TryLoadChunk(Name, pos, out var data)
            ? ChunkCodec.Deserialize(pos, data)
            : chunkGenerator.Generate(pos);
        chunk.ClearDirty(); // a freshly loaded/generated chunk is the baseline, not a pending change
        return chunk;
    }

    public int LoadedChunkCount => loadedChunks.Count;

    /// <summary>Persists every chunk modified since its last save and marks them clean. No-op without a
    /// store. Returns the number of chunks written.</summary>
    public int Save() {
        if (store is null) return 0;
        int saved = 0;
        foreach (var chunk in loadedChunks.Values)
            if (chunk.Dirty) {
                store.SaveChunk(Name, chunk.Position, ChunkCodec.Serialize(chunk));
                chunk.ClearDirty();
                saved++;
            }
        SaveEntities(); // loose world entities (dropped items) aren't chunk-dirty-tied; persist them every save
        return saved;
    }

    /// <summary>Persists this world's loose, world-persistent entities (dropped items, ...) as one blob (replacing
    /// the previous one), so they survive a restart. Returns the number saved; no-op without a store. Call on the
    /// tick thread (the drain phase), like chunk saves.</summary>
    public int SaveEntities() {
        if (store is null) return 0;
        var persisted = new List<ArchEntity>();
        Ecs.Query(in TypedEntityQuery, (ArchEntity e, ref TypeEntityDescriptor tag) => {
            if (tag.Type.Persisted) persisted.Add(e);
        });

        using var ms = new MemoryStream();
        var s = new MinecraftStream(ms);
        s.WriteUByte(EntitiesVersion);
        s.WriteVarInt(persisted.Count);
        foreach (var e in persisted) {
            s.WriteString(Ecs.Get<TypeEntityDescriptor>(e).Type.Id.Full); // the kind, so load spawns the right blueprint
            var blob = EntityCodec.Encode(Ecs, e);
            s.WriteVarInt(blob.Length);
            s.Write(blob, 0, blob.Length);
        }
        store.SaveWorldEntities(Name, ms.ToArray());
        return persisted.Count;
    }

    /// <summary>Respawns this world's saved loose entities onto fresh blueprint instances. Call ONCE, at startup,
    /// before the world ticks (spawning is a structural ECS change). Returns the number spawned; no-op without a
    /// store or saved data. An entity of an unregistered kind (a removed mod's) is skipped.</summary>
    public int LoadEntities() {
        if (store is null || store.LoadWorldEntities(Name) is not { } data) return 0;
        using var ms = new MemoryStream(data, writable: false);
        var s = new MinecraftStream(ms);
        if (s.ReadUByte() is var version && version != EntitiesVersion)
            throw new NotSupportedException($"Unknown world-entity format version {version}.");

        int count = s.ReadVarInt();
        int spawned = 0;
        for (int i = 0; i < count; i++) {
            var typeId = s.ReadString();
            var blob = s.ReadBytes(s.ReadVarInt());
            if (EntityRegistry.FromName(typeId) is not { } type) continue; // unknown kind - skip (its blob is consumed)
            var entity = Spawn(type, default);          // blueprint at origin; the blob's transform overwrites it
            EntityCodec.Apply(Ecs, entity, blob);
            var t = Ecs.Get<TransformEntityComponent>(entity);
            Entities.Update(entity, t.X, t.Y, t.Z);     // re-file at the restored position
            spawned++;
        }
        return spawned;
    }

    /// <summary>Drops loaded chunks outside every kept centre (saving dirty ones first) to bound memory/disk.
    /// A dirty chunk with no store is kept rather than lose the edit. Call on the tick thread.</summary>
    public int EvictChunks(IReadOnlyList<(long X, long Z)>? keptCenters, int keepRadius) {
        int evicted = 0;
        foreach (var (pos, chunk) in loadedChunks) {
            if (IsKept(pos.X, pos.Z, keptCenters, keepRadius))
                continue;
            if (chunk.Dirty) {
                if (store is null) continue; // no backend - keep it rather than lose the edit
                store.SaveChunk(Name, pos, ChunkCodec.Serialize(chunk));
            }
            loadedChunks.TryRemove(pos, out _);
            evicted++;
        }
        return evicted;

        static bool IsKept(long cx, long cz, IReadOnlyList<(long X, long Z)>? centers, int radius) {
            if (centers is null) return false; // no viewers in this world -> evict everything
            foreach (var c in centers)
                if (System.Math.Abs(cx - c.X) <= radius && System.Math.Abs(cz - c.Z) <= radius)
                    return true;
            return false;
        }
    }

    // -- Blocks --------------------------------------------------------------
    public BlockType GetBlock(Vector3i pos) {
        var chunk = GetChunk(pos.ToChunk());
        pos = pos.ToLocal();
        return chunk.GetBlock(pos.X, pos.Y, pos.Z);
    }

    public void SetBlock(Vector3i pos, BlockType block) {
        var chunk = GetChunk(pos.ToChunk());
        pos = pos.ToLocal();
        chunk.SetBlock(pos.X, pos.Y, pos.Z, block);
    }

    /// <summary>The block entity at <paramref name="pos"/>, or null if that block has no instance data.</summary>
    public BlockEntity? GetBlockEntity(Vector3i pos) => GetChunk(pos.ToChunk()).GetBlockEntity(pos);
    public void SetBlockEntity(BlockEntity entity) => GetChunk(entity.Position.ToChunk()).SetBlockEntity(entity);
    public bool RemoveBlockEntity(Vector3i pos) => GetChunk(pos.ToChunk()).RemoveBlockEntity(pos);

    /// <summary>Marks the chunk containing <paramref name="pos"/> dirty so it persists - call after mutating a block
    /// entity's components in place (the block edit paths already mark dirty themselves).</summary>
    public void MarkDirty(Vector3i pos) => GetChunk(pos.ToChunk()).MarkDirty();

    /// <summary>The block entity at <paramref name="pos"/>, creating and initializing one (via the block's
    /// <see cref="IBlockEntityDescriptor"/>) if the block carries a block entity but none exists yet - the one
    /// funnel for materializing instances, so the initializer runs exactly once. Null if the block at
    /// <paramref name="pos"/> carries no block entity.</summary>
    public BlockEntity? GetOrCreateBlockEntity(Vector3i pos) {
        if (GetBlockEntity(pos) is { } existing) return existing;
        var block = GetBlock(pos);
        if (block.GetAll<IBlockEntityDescriptor>().FirstOrDefault() is not { } descriptor) return null;
        var entity = new BlockEntity(pos, block);
        descriptor.Initialize(entity);
        SetBlockEntity(entity);
        return entity;
    }

    /// <summary>The block state at <paramref name="pos"/>, or null if it's the type's default state.</summary>
    public BlockState? GetBlockState(Vector3i pos) => GetChunk(pos.ToChunk()).GetBlockState(pos.ToLocal());
    public void SetBlockState(Vector3i pos, BlockState? state) => GetChunk(pos.ToChunk()).SetBlockState(pos.ToLocal(), state);

    /// <summary>Breaks the block at <paramref name="pos"/>: replaces it with air, fires the block's break
    /// behaviors, and spawns its drop. Returns the block that was broken (air if nothing was there).</summary>
    public BlockType BreakBlock(Vector3i pos, PlayerContext? actor = null) {
        var block = GetBlock(pos);
        if (block.IsAir)
            return block;

        SetBlock(pos, BlockRegistry.Air);

        var ctx = new BlockContext { World = this, Position = pos, Block = block, Actor = actor };
        foreach (var b in block.GetAll<IOnBroken>())
            b.OnBroken(in ctx);

        if (block.Drop is { } drop) {
            // A self-drop carries item-identity state (e.g. wool colour) but resets placement state (facing),
            // so a purely-placement state drops with none and re-orients on re-placement.
            if (drop.Type == block && GetBlockState(pos) is { } state) {
                var dropState = state.ForDrop();
                if (!dropState.Matches(new BlockState(block)))
                    drop = drop.WithState(dropState);
            }
            SpawnDroppedItem(pos, drop);
        }

        // Scatter container contents before the block entity goes away.
        if (GetBlockEntity(pos) is { } entity && entity.TryGet<InventoryComponent>(out var contents))
            for (int i = 0; i < contents.Size; i++)
                if (!contents[i].IsEmpty) SpawnDroppedItem(pos, contents[i]);

        RemoveBlockEntity(pos);
        SetBlockState(pos, null);
        return block;
    }

    /// <summary>Places a block at <paramref name="pos"/> if the space is air AND no solid entity is standing in
    /// it - like vanilla, you can't place a block inside a player's (or mob's) collision box.</summary>
    public bool PlaceBlock(Vector3i pos, BlockType block) {
        if (!GetBlock(pos).IsAir)
            return false;
        if (IsObstructedByEntity(pos))
            return false;

        SetBlock(pos, block);
        return true;
    }

    // Bounds the spatial-index scan for placement obstruction (the precise AABB test decides actual overlap).
    // Covers a standing player/mob (~3 blocks from centre); revisit if a much larger Placement entity is added.
    const double PlacementScanRadius = 3.0;

    /// <summary>Whether an entity whose hitbox declares <see cref="CollisionUsage.Placement"/> overlaps the unit
    /// cube at <paramref name="pos"/>. Any such entity (players, future mobs) blocks placement; entities without
    /// the flag (dropped items, falling blocks) don't - no per-type special-casing.</summary>
    bool IsObstructedByEntity(Vector3i pos) {
        double bx = pos.X, by = pos.Y, bz = pos.Z; // block cube spans [b, b+1] on each axis
        var candidates = new List<ArchEntity>();
        Entities.Near(bx + 0.5, by + 0.5, bz + 0.5, PlacementScanRadius, candidates);
        foreach (var e in candidates) {
            if (!Ecs.Has<HitboxEntityComponent>(e)) continue;
            var box = Ecs.Get<HitboxEntityComponent>(e);
            if (!box.Usage.HasFlag(CollisionUsage.Placement)) continue;
            var t = Ecs.Get<TransformEntityComponent>(e);
            double hw = box.HalfWidth, h = box.Height, tx = t.X, ty = t.Y, tz = t.Z;
            // AABB overlap between the entity hitbox [X+/-hw]x[Y,Y+h]x[Z+/-hw] and the block cube.
            if (tx - hw < bx + 1 && tx + hw > bx &&
                ty      < by + 1 && ty + h   > by &&
                tz - hw < bz + 1 && tz + hw > bz)
                return true;
        }
        return false;
    }

    // -- Entities ------------------------------------------------------------
    public SpatialIndex Entities { get; }

    /// <summary>Materialises an entity of <paramref name="type"/> from its blueprint at <paramref name="transform"/>:
    /// creates and type-tags it (<see cref="EntityType.Create"/>), sets its transform, and registers it in the spatial
    /// index. Callers then assign any per-instance components via <c>Ecs.Get&lt;T&gt;(e) = ...</c>. This is the single
    /// path every spawn flows through, so the spatial-index registration can never be forgotten.</summary>
    public ArchEntity Spawn(EntityType type, TransformEntityComponent transform) {
        var entity = type.Create(Ecs);
        Ecs.Get<TransformEntityComponent>(entity) = transform;
        Entities.Add(entity, transform.X, transform.Y, transform.Z);
        return entity;
    }

    /// <summary>Spawns a player entity at the flat-world surface and returns its handle.</summary>
    public ArchEntity SpawnPlayer(ulong clientId, string name, Guid uuid, int entityId, byte[]? saved = null) =>
        Player.Spawn(this, clientId, name, uuid, entityId, new TransformEntityComponent(0.5, WorldDefaults.SurfaceY, 0.5), saved);

    /// <summary>Spawns a dropped-item entity centred on a block with a small random upward pop.</summary>
    public ArchEntity SpawnDroppedItem(Vector3i blockPos, ItemStack stack) {
        var rng = Random.Shared;
        return SpawnItem(blockPos.X + 0.5, blockPos.Y + 0.25, blockPos.Z + 0.5,
            new VelocityEntityComponent(rng.NextSingle() * 0.2f - 0.1f, 0.2f, rng.NextSingle() * 0.2f - 0.1f), stack, pickupDelay: 10);
    }

    /// <summary>Spawns a dropped-item entity at an explicit world position with an explicit velocity and
    /// pickup delay - the primitive behind both block-break drops and player tosses.</summary>
    public ArchEntity SpawnItem(double x, double y, double z, VelocityEntityComponent velocity, ItemStack stack, int pickupDelay) {
        var entity = Spawn(EntityRegistry.Item, new TransformEntityComponent(x, y, z));
        Ecs.Get<VelocityEntityComponent>(entity) = velocity;
        Ecs.Get<PickupEntityComponent>(entity) = new PickupEntityComponent { Stack = stack, Age = 0, PickupDelay = pickupDelay };
        return entity;
    }

    // Player eye height (the toss origin) and the speed an item leaves the hand at.
    const double EyeHeight = 1.62;
    const double TossSpeed = 0.3;

    /// <summary>Spawns <paramref name="stack"/> as a dropped item thrown from <paramref name="t"/>'s eye in
    /// its look direction (the "press Q" toss), with a pickup delay so it can't fly straight back in.
    /// Returns the entity (null for an empty stack).</summary>
    public ArchEntity? TossItem(TransformEntityComponent t, ItemStack stack) {
        if (stack.IsEmpty) return null;
        double yaw = t.Yaw * System.Math.PI / 180.0, pitch = t.Pitch * System.Math.PI / 180.0;
        double cosPitch = System.Math.Cos(pitch);
        var velocity = new VelocityEntityComponent(
            -System.Math.Sin(yaw) * cosPitch * TossSpeed,
            -System.Math.Sin(pitch) * TossSpeed + 0.1,
            System.Math.Cos(yaw) * cosPitch * TossSpeed);
        return SpawnItem(t.X, t.Y + EyeHeight - 0.3, t.Z, velocity, stack, pickupDelay: 40);
    }

    /// <summary>Spawns a <c>falling_block</c> entity for <paramref name="block"/> at the centre of
    /// <paramref name="cell"/>. EntityPhysicsSystem pulls it down; FallingBlockSystem re-places it (or drops
    /// it) on landing. The caller must have cleared the source cell and announced that change.</summary>
    public ArchEntity SpawnFallingBlock(Vector3i cell, BlockType block) {
        var entity = Spawn(EntityRegistry.FallingBlock, new TransformEntityComponent(cell.X + 0.5, cell.Y, cell.Z + 0.5));
        Ecs.Get<FallingBlockEntityComponent>(entity).Block = block;
        return entity;
    }

    /// <summary>Despawns an entity: drops it from the spatial index, then destroys it in the ECS.
    /// Route all entity destruction through here so the index never holds a dead handle.</summary>
    public void DestroyEntity(ArchEntity entity) {
        Entities.Remove(entity);
        Ecs.Destroy(entity);
    }

    public int PlayerCount => Ecs.CountEntities(in PlayerQuery);

    /// <summary>Tears the world down: stops ticking, releases loaded chunks, and frees the ECS storage.
    /// Not idempotent - the world must not be used afterwards.</summary>
    public void Unload() {
        IsActive = false;
        loadedChunks.Clear();
        ArchWorld.Destroy(Ecs);
    }

    public void Tick() {
        if (!IsActive) return;

        foreach (var system in systems)
            system.Tick();

        // Block entities (furnaces, ...) per loaded chunk.
        foreach (var chunk in loadedChunks.Values)
            chunk.Tick();
    }
}
