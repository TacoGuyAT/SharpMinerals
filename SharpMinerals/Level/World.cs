using System.Collections.Concurrent;
using Arch.Core;
using SharpMinerals.Blocks;
using SharpMinerals.Blocks.Components;
using SharpMinerals.Components;
using SharpMinerals.Entities;
using SharpMinerals.Entities.Components;
using SharpMinerals.Items;
using SharpMinerals.Math;
using ArchWorld = Arch.Core.World;
using ArchEntity = Arch.Core.Entity;

namespace SharpMinerals.Level;

/// <summary>
/// A single dimension. Owns an Arch ECS <see cref="ArchWorld"/> holding every
/// entity in it and a lazily-generated grid of cuboid chunks. <see cref="Tick"/>
/// runs the per-tick systems; block reads/writes go through the chunk grid.
/// </summary>
public class World : ITickable {
    // Reused query descriptions — building these is cheap but doing it once is tidier.
    static readonly QueryDescription MovementQuery = new QueryDescription().WithAll<Transform, Velocity>();
    static readonly QueryDescription PlayerQuery = new QueryDescription().WithAll<NetworkedPlayer>();
    static readonly QueryDescription DropQuery = new QueryDescription().WithAll<DroppedItem, Velocity>();
    static readonly QueryDescription DropPhysicsQuery = new QueryDescription().WithAll<DroppedItem, Transform, Velocity>();
    // Only colliders (players) scan — dropped items never check around themselves.
    static readonly QueryDescription ColliderQuery = new QueryDescription().WithAll<Transform, CollisionFeedback>();

    // Half-extent of a dropped item's collision box.
    const double ItemHalfSize = 0.2;

    const double DropGravity = 0.04;

    readonly IChunkGenerator chunkGenerator;
    readonly ConcurrentDictionary<Vector3i, Chunk> loadedChunks = new();

    public string Name { get; }

    /// <summary>The ECS backing store. Entities are created/queried through this.</summary>
    public ArchWorld Ecs { get; } = ArchWorld.Create();

    public bool IsActive { get; set; } = true;

    public World(string name, IChunkGenerator chunkGenerator) {
        Name = name;
        this.chunkGenerator = chunkGenerator;
    }

    // ── Chunks ──────────────────────────────────────────────────────────────
    static Vector3i ChunkOf(Vector3i world) => new(
        MathHelper.FloorDiv(world.X, Chunk.Size),
        MathHelper.FloorDiv(world.Y, Chunk.Size),
        MathHelper.FloorDiv(world.Z, Chunk.Size));

    /// <summary>Gets a chunk by chunk coordinate, generating it on first access.</summary>
    public Chunk GetChunk(Vector3i chunkPosition) =>
        loadedChunks.GetOrAdd(chunkPosition, chunkGenerator.Generate);

    public int LoadedChunkCount => loadedChunks.Count;

    // ── Blocks ──────────────────────────────────────────────────────────────
    public BlockType GetBlock(Vector3i pos) {
        var chunk = GetChunk(ChunkOf(pos));
        return chunk.GetBlock(LocalX(pos.X), LocalX(pos.Y), LocalX(pos.Z));
    }

    public void SetBlock(Vector3i pos, BlockType block) {
        var chunk = GetChunk(ChunkOf(pos));
        chunk.SetBlock(LocalX(pos.X), LocalX(pos.Y), LocalX(pos.Z), block);
    }

    static int LocalX(long world) => MathHelper.FloorMod(world, Chunk.Size);

    /// <summary>The block entity at <paramref name="pos"/>, or null if that block has no instance data.</summary>
    public BlockEntity? GetBlockEntity(Vector3i pos) => GetChunk(ChunkOf(pos)).GetBlockEntity(pos);
    public void SetBlockEntity(BlockEntity entity) => GetChunk(ChunkOf(entity.Position)).SetBlockEntity(entity);
    public bool RemoveBlockEntity(Vector3i pos) => GetChunk(ChunkOf(pos)).RemoveBlockEntity(pos);

    /// <summary>
    /// Breaks the block at <paramref name="pos"/>: replaces it with air, fires the
    /// block's break behaviors, and — if it has a <see cref="Drops"/> component — spawns
    /// the dropped item. Returns the block that was broken (air if nothing was there).
    /// </summary>
    public BlockType BreakBlock(Vector3i pos) {
        var block = GetBlock(pos);
        if (block.IsAir)
            return block;

        SetBlock(pos, BlockRegistry.Air);

        var ctx = new BlockContext { World = this, Position = pos, Block = block, Actor = default };
        Behavior.FireBroken(block, in ctx);

        if (block.TryGet<Drops>(out var drops))
            SpawnDroppedItem(pos, new ItemStack(drops.Item));

        // Scatter any container contents (e.g. a chest) before the block entity goes away.
        if (GetBlockEntity(pos) is { } entity && entity.TryGet<Inventory>(out var contents))
            for (int i = 0; i < contents.Size; i++)
                if (!contents[i].IsEmpty) SpawnDroppedItem(pos, contents[i]);

        RemoveBlockEntity(pos);
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
    /// <summary>Spawns a player entity at the flat-world surface and returns its handle.</summary>
    public ArchEntity SpawnPlayer(ulong clientId, string name, Guid uuid, int entityId) =>
        Player.Spawn(this, clientId, name, uuid, entityId, new Transform(0.5, FlatChunkGenerator.SurfaceY, 0.5));

    /// <summary>Spawns a dropped-item entity centred on a block with a small upward pop.</summary>
    public ArchEntity SpawnDroppedItem(Vector3i blockPos, ItemStack stack) =>
        Ecs.Create(
            new Transform(blockPos.X + 0.5, blockPos.Y + 0.25, blockPos.Z + 0.5),
            new Velocity(0, 0.2, 0),
            new DroppedItem { Stack = stack, Age = 0, PickupDelay = 10 });

    /// <summary>Number of player entities currently in this world.</summary>
    public int PlayerCount => Ecs.CountEntities(in PlayerQuery);

    public void Tick() {
        if (!IsActive) return;

        // Dropped items fall under gravity until they (crudely) come to rest, and age.
        Ecs.Query(in DropQuery, (ref DroppedItem drop, ref Velocity v) => {
            drop.Age++;
            if (drop.PickupDelay > 0) drop.PickupDelay--;
            v.Y -= DropGravity;
        });

        // Movement system: integrate velocity into the transform for everything that moves.
        Ecs.Query(in MovementQuery, (ref Transform t, ref Velocity v) => {
            t.X += v.X;
            t.Y += v.Y;
            t.Z += v.Z;
        });

        // Rest dropped items on the surface so they stay reachable for pickup (they have
        // no terrain physics otherwise and would sink through the floor).
        Ecs.Query(in DropPhysicsQuery, (ref Transform t, ref Velocity v) => {
            var feet = new Vector3i((int)System.Math.Floor(t.X), (int)System.Math.Floor(t.Y), (int)System.Math.Floor(t.Z));
            if (!GetBlock(feet).IsAir) { t.Y = System.Math.Floor(t.Y) + 1.0; v.Y = 0; }
        });

        DetectCollisions();

        foreach (var chunk in loadedChunks.Values)
            chunk.Tick();
    }

    /// <summary>
    /// Player-driven collision: each entity with a <see cref="CollisionFeedback"/> box
    /// scans the dropped items and records the ones it overlaps. Items are passive — they
    /// never scan, so cost is players × items, not items × everything.
    /// </summary>
    void DetectCollisions() {
        var items = new List<(ArchEntity Entity, double X, double Y, double Z)>();
        Ecs.Query(in DropPhysicsQuery, (ArchEntity e, ref Transform t) => items.Add((e, t.X, t.Y, t.Z)));

        Ecs.Query(in ColliderQuery, (ref Transform t, ref CollisionFeedback c) => {
            c.Touching.Clear();
            if (items.Count == 0) return;
            double reach = c.Width / 2 + ItemHalfSize;
            foreach (var it in items)
                if (System.Math.Abs(t.X - it.X) <= reach && System.Math.Abs(t.Z - it.Z) <= reach
                    && it.Y >= t.Y - 1.0 && it.Y <= t.Y + c.Height)
                    c.Touching.Add(it.Entity);
        });
    }
}
