using System.Collections.Concurrent;
using SharpMinerals.Entities.Components;
using SharpMinerals.Math;
using ArchEntity = Arch.Core.Entity;

namespace SharpMinerals.Level;

/// <summary>A per-<see cref="World"/> spatial index of entities, bucketed by chunk-cube, so "what's near
/// here?" scans only the buckets overlapping the query region. Maintained incrementally via <see cref="Add"/>/
/// <see cref="Remove"/>/<see cref="Update"/>. Concurrent: the lifecycle spans the tick and network threads.</summary>
public sealed class SpatialIndex {
    // chunk-cube coord -> set of entities whose position falls in that cube.
    readonly ConcurrentDictionary<Vector3i, ConcurrentDictionary<ArchEntity, byte>> buckets = new();
    // entity -> the cube it's currently filed under, so Remove/Update need no caller-supplied old position.
    readonly ConcurrentDictionary<ArchEntity, Vector3i> cellOf = new();

    readonly World world;

    public SpatialIndex(World world) => this.world = world;

    /// <summary>An entity crossed a chunk COLUMN boundary (its X/Z chunk changed; pure-Y moves don't fire it).
    /// The entity tracker uses this to move the entity between viewers' loaded-column sets.</summary>
    public event Action<ArchEntity, (Mint X, Mint Z), (Mint X, Mint Z)>? EntityChangedColumn;

    static (Mint X, Mint Z) Column(Vector3i cell) => (cell.X, cell.Z);

    /// <summary>The chunk-cube coordinate that contains a world position. Must be the GLOBAL chunk coord
    /// (monotonic, so the range walk in <see cref="ForEachCandidate"/> works); ToLocal would wrap at chunk
    /// boundaries and silently skip cells.</summary>
    public static Vector3i CellOf(double x, double y, double z) => new Vector3i(
        (Mint)System.Math.Floor(x),
        (Mint)System.Math.Floor(y),
        (Mint)System.Math.Floor(z)
    ).ToChunk();

    // -- Maintenance -----------------------------------------------------------
    public void Add(ArchEntity entity, double x, double y, double z) {
        var cell = CellOf(x, y, z);
        Bucket(cell)[entity] = 0;
        cellOf[entity] = cell;
    }

    public void Remove(ArchEntity entity) {
        if (cellOf.TryRemove(entity, out var cell) && buckets.TryGetValue(cell, out var set))
            set.TryRemove(entity, out _);
    }

    /// <summary>Re-files an entity that moved; a no-op unless it crossed a chunk boundary.</summary>
    public void Update(ArchEntity entity, double x, double y, double z) {
        var cell = CellOf(x, y, z);
        bool known = cellOf.TryGetValue(entity, out var old);
        if (known) {
            if (old == cell) return; // same cube - nothing to do
            if (buckets.TryGetValue(old, out var oldSet)) oldSet.TryRemove(entity, out _);
        }
        Bucket(cell)[entity] = 0;
        cellOf[entity] = cell;
        if (known && Column(old) != Column(cell)) EntityChangedColumn?.Invoke(entity, Column(old), Column(cell));
    }

    ConcurrentDictionary<ArchEntity, byte> Bucket(Vector3i cell) =>
        buckets.GetOrAdd(cell, static _ => new ConcurrentDictionary<ArchEntity, byte>());

    // -- Queries ---------------------------------------------------------------
    /// <summary>The entities filed in one chunk-cube (the unit of per-chunk processing) - a snapshot.</summary>
    public IReadOnlyCollection<ArchEntity> InChunk(Vector3i chunkCoord) =>
        buckets.TryGetValue(chunkCoord, out var set) ? set.Keys.ToArray() : System.Array.Empty<ArchEntity>();

    /// <summary>The chunk-cubes that currently hold at least one entity (for sweeping per-chunk work).</summary>
    public IEnumerable<Vector3i> OccupiedChunks => buckets.Keys;

    /// <summary>Appends every live entity within <paramref name="radius"/> blocks of the point to
    /// <paramref name="results"/> (Euclidean).</summary>
    public void Near(double x, double y, double z, double radius, ICollection<ArchEntity> results) {
        double r2 = radius * radius;
        ForEachCandidate(x - radius, y - radius, z - radius, x + radius, y + radius, z + radius, entity => {
            var t = world.Ecs.Get<TransformEntityComponent>(entity);
            double dx = t.X - x, dy = t.Y - y, dz = t.Z - z;
            if (dx * dx + dy * dy + dz * dz <= r2) results.Add(entity);
        });
    }

    /// <summary>Appends every live entity whose position lies inside the box [min,max] to <paramref name="results"/>.</summary>
    public void InAabb(double minX, double minY, double minZ, double maxX, double maxY, double maxZ, ICollection<ArchEntity> results) {
        ForEachCandidate(minX, minY, minZ, maxX, maxY, maxZ, entity => {
            var t = world.Ecs.Get<TransformEntityComponent>(entity);
            if (t.X >= minX && t.X <= maxX && t.Y >= minY && t.Y <= maxY && t.Z >= minZ && t.Z <= maxZ)
                results.Add(entity);
        });
    }

    // Walks the chunk-cubes overlapping a box and invokes the action for each live entity (dead entries,
    // from a destroy that raced an in-flight query, are skipped).
    void ForEachCandidate(double minX, double minY, double minZ, double maxX, double maxY, double maxZ,
                          System.Action<ArchEntity> action) {
        var lo = CellOf(minX, minY, minZ);
        var hi = CellOf(maxX, maxY, maxZ);
        for (Mint cx = lo.X; cx <= hi.X; cx++)
            for (Mint cy = lo.Y; cy <= hi.Y; cy++)
                for (Mint cz = lo.Z; cz <= hi.Z; cz++)
                    if (buckets.TryGetValue(new Vector3i(cx, cy, cz), out var set))
                        foreach (var kv in set) // lock-free, no Keys snapshot allocation
                            if (world.Ecs.IsAlive(kv.Key)) action(kv.Key);
    }
}
