using System.Collections.Concurrent;
using Arch.Core;
using SharpMinerals.Blocks;
using SharpMinerals.Blocks.Descriptors;
using SharpMinerals.Components;
using SharpMinerals.Entities;
using SharpMinerals.Entities.Components;
using SharpMinerals.Events;
using SharpMinerals.Items;
using SharpMinerals.Math;
using SharpMinerals.Persistence;
using ArchWorld = Arch.Core.World;
using ArchEntity = Arch.Core.Entity;
using SharpMinerals.Entities.Descriptors;

namespace SharpMinerals.Level;

/// <summary>A single dimension: owns an Arch ECS <see cref="ArchWorld"/> and a lazily-generated grid of
/// cuboid chunks. <see cref="Tick"/> runs the per-tick systems; block reads/writes go through the chunk grid.</summary>
public class World : ITickable {
    static readonly QueryDescription PlayerQuery = new QueryDescription().WithAll<NetPlayerEntityComponent>();

    // Half-extent of a dropped item's collision box.
    const double ItemHalfSize = 0.125;

    readonly IChunkGenerator chunkGenerator;
    readonly IWorldStore? store;
    readonly ConcurrentDictionary<Vector3i, Chunk> loadedChunks = new();

    // The per-tick entity systems, run in registration order each Tick.
    readonly List<ITickable> systems;

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
        };
    }

    // ── Chunks ──────────────────────────────────────────────────────────────
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
        return saved;
    }

    /// <summary>Drops loaded chunks outside every kept centre (saving dirty ones first) to bound memory/disk.
    /// A dirty chunk with no store is kept rather than lose the edit. Call on the tick thread.</summary>
    public int EvictChunks(IReadOnlyList<(long X, long Z)>? keptCenters, int keepRadius) {
        int evicted = 0;
        foreach (var (pos, chunk) in loadedChunks) {
            if (IsKept(pos.X, pos.Z, keptCenters, keepRadius))
                continue;
            if (chunk.Dirty) {
                if (store is null) continue; // no backend — keep it rather than lose the edit
                store.SaveChunk(Name, pos, ChunkCodec.Serialize(chunk));
            }
            loadedChunks.TryRemove(pos, out _);
            evicted++;
        }
        return evicted;

        static bool IsKept(long cx, long cz, IReadOnlyList<(long X, long Z)>? centers, int radius) {
            if (centers is null) return false; // no viewers in this world → evict everything
            foreach (var c in centers)
                if (System.Math.Abs(cx - c.X) <= radius && System.Math.Abs(cz - c.Z) <= radius)
                    return true;
            return false;
        }
    }

    // ── Blocks ──────────────────────────────────────────────────────────────
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

    /// <summary>The block state at <paramref name="pos"/>, or null if it's the type's default state.</summary>
    public BlockState? GetBlockState(Vector3i pos) => GetChunk(pos.ToChunk()).GetBlockState(pos.ToLocal());
    public void SetBlockState(Vector3i pos, BlockState? state) => GetChunk(pos.ToChunk()).SetBlockState(pos.ToLocal(), state);

    /// <summary>Breaks the block at <paramref name="pos"/>: replaces it with air, fires the block's break
    /// behaviors, and spawns its drop. Returns the block that was broken (air if nothing was there).</summary>
    public BlockType BreakBlock(Vector3i pos) {
        var block = GetBlock(pos);
        if (block.IsAir)
            return block;

        SetBlock(pos, BlockRegistry.Air);

        var ctx = new BlockContext { World = this, Position = pos, Block = block, Actor = default };
        Behavior.FireBroken(block, in ctx);

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

    /// <summary>Places a block at <paramref name="pos"/> if the space is currently air.</summary>
    public bool PlaceBlock(Vector3i pos, BlockType block) {
        if (!GetBlock(pos).IsAir)
            return false;

        SetBlock(pos, block);
        return true;
    }

    // ── Entities ────────────────────────────────────────────────────────────
    public SpatialIndex Entities { get; }

    /// <summary>Spawns a player entity at the flat-world surface and returns its handle.</summary>
    public ArchEntity SpawnPlayer(ulong clientId, string name, Guid uuid, int entityId, PlayerState? saved = null) {
        var entity = Player.Spawn(this, clientId, name, uuid, entityId, new TransformEntityComponent(0.5, FlatChunkGenerator.SurfaceY, 0.5), saved);
        var t = Ecs.Get<TransformEntityComponent>(entity);
        Entities.Add(entity, t.X, t.Y, t.Z); // restored spawns may not be at the default position
        return entity;
    }

    /// <summary>Spawns a dropped-item entity centred on a block with a small random upward pop.</summary>
    public ArchEntity SpawnDroppedItem(Vector3i blockPos, ItemStack stack) {
        var rng = Random.Shared;
        return SpawnItem(blockPos.X + 0.5, blockPos.Y + 0.25, blockPos.Z + 0.5,
            new VelocityEntityComponent(rng.NextSingle() * 0.2f - 0.1f, 0.2f, rng.NextSingle() * 0.2f - 0.1f), stack, pickupDelay: 10);
    }

    /// <summary>Spawns a dropped-item entity at an explicit world position with an explicit velocity and
    /// pickup delay — the primitive behind both block-break drops and player tosses.</summary>
    public ArchEntity SpawnItem(double x, double y, double z, VelocityEntityComponent velocity, ItemStack stack, int pickupDelay) {
        var entity = Ecs.Create(
            new TransformEntityComponent(x, y, z),
            velocity,
            new ColliderEntityComponent(ItemHalfSize * 2, ItemHalfSize * 2),
            new GravityEntityComponent(),
            new TypeEntityDescriptor { Type = EntityRegistry.Item },
            new PickupEntityComponent { Stack = stack, Age = 0, PickupDelay = pickupDelay });
        Entities.Add(entity, x, y, z);
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

    // A falling block's collision box (vanilla falling_block is 0.98³).
    const double FallingBlockSize = 0.98;

    /// <summary>Spawns a <c>falling_block</c> entity for <paramref name="block"/> at the centre of
    /// <paramref name="cell"/>. EntityPhysicsSystem pulls it down; FallingBlockSystem re-places it (or drops
    /// it) on landing. The caller must have cleared the source cell and announced that change.</summary>
    public ArchEntity SpawnFallingBlock(Vector3i cell, BlockType block) {
        double x = cell.X + 0.5, y = cell.Y, z = cell.Z + 0.5;
        var entity = Ecs.Create(
            new TransformEntityComponent(x, y, z),
            new VelocityEntityComponent(0, 0, 0),
            new ColliderEntityComponent(FallingBlockSize, FallingBlockSize),
            new GravityEntityComponent(),
            new BlockCollisionFeedbackEntityComponent(),
            new TypeEntityDescriptor { Type = EntityRegistry.FallingBlock },
            new FallingBlockEntityComponent { Block = block, EntityId = 0 });
        Entities.Add(entity, x, y, z);
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
    /// Not idempotent — the world must not be used afterwards.</summary>
    public void Unload() {
        IsActive = false;
        loadedChunks.Clear();
        ArchWorld.Destroy(Ecs);
    }

    public void Tick() {
        if (!IsActive) return;

        foreach (var system in systems)
            system.Tick();

        // Block entities (furnaces, …) per loaded chunk.
        foreach (var chunk in loadedChunks.Values)
            chunk.Tick();
    }
}
