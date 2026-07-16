using System.Text;
using RocksDbSharp;
using SharpMinerals.Math;

namespace SharpMinerals.Persistence.RocksDb;

/// <summary>
/// Disk-backed <see cref="IWorldStore"/> on RocksDB, one database (directory) per world: one key per chunk
/// (<c>"x,y,z"</c> bytes), value is the <see cref="ChunkCodec"/> blob. Survives restarts. Lives in the
/// adapter assembly so the core carries no native dependency.
/// </summary>
public sealed class RocksDbWorldStore : IWorldStore, IDisposable {
    // Fully qualified: the enclosing namespace ends in ".RocksDb", shadowing the type name.
    readonly RocksDbSharp.RocksDb db;

    /// <summary>Opens the world's database directory, creating it if missing.</summary>
    public RocksDbWorldStore(string path) {
        Directory.CreateDirectory(path);
        var options = new DbOptions().SetCreateIfMissing(true);
        db = RocksDbSharp.RocksDb.Open(options, path);
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
