using System.Text;
using SharpMinerals.Math;
// Aliased through global:: - the enclosing namespace ends in ".MinRocksDb", which would otherwise shadow it.
using RocksDbKv = global::MinRocksDb.RocksDbKv;

namespace SharpMinerals.Persistence.MinRocksDb;

/// <summary>
/// Native-AOT-safe disk-backed <see cref="IWorldStore"/> on <see cref="RocksDbKv"/>, one database
/// (directory) per world: one key per chunk (<c>"x,y,z"</c> bytes), value is the <see cref="ChunkCodec"/>
/// blob. Same on-disk layout as <c>RocksDbWorldStore</c> (the RocksDbSharp adapter), so a world is portable
/// between the JIT and AOT builds.
/// </summary>
public sealed class MinRocksDbWorldStore : IWorldStore, IDisposable {
    readonly RocksDbKv db;

    /// <summary>Opens the world's database directory, creating it if missing.</summary>
    public MinRocksDbWorldStore(string path) {
        Directory.CreateDirectory(path);
        db = new RocksDbKv(path);
    }

    static byte[] Key(Vector3i c) => Encoding.UTF8.GetBytes($"{c.X},{c.Y},{c.Z}");
    // Never collides with a chunk key (those are only digits, '-' and ',').
    static readonly byte[] EntitiesKey = "entities"u8.ToArray();

    public void SaveChunk(Vector3i chunk, byte[] data) => db.Put(Key(chunk), data);

    public bool TryLoadChunk(Vector3i chunk, out byte[] data) {
        var bytes = db.Get(Key(chunk));
        if (bytes is null) {
            data = default!;
            return false;
        }
        data = bytes;
        return true;
    }

    public void SaveEntities(byte[] data) => db.Put(EntitiesKey, data);

    public byte[]? LoadEntities() => db.Get(EntitiesKey);

    public void Dispose() => db.Dispose();
}
