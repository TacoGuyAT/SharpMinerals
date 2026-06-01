using System.Text;
using RocksDbSharp;
using SharpMinerals.Math;

namespace SharpMinerals.Persistence.RocksDb;

/// <summary>
/// Disk-backed <see cref="IWorldStore"/> on RocksDB: one key per chunk
/// (<c>"&lt;world&gt;:x,y,z"</c> bytes), value is the <see cref="ChunkCodec"/> blob. Survives
/// restarts. Lives in the adapter assembly so the core carries no native dependency.
/// </summary>
public sealed class RocksDbWorldStore : IWorldStore, IDisposable {
    // Fully qualified: the enclosing namespace ends in ".RocksDb", shadowing the type name.
    readonly RocksDbSharp.RocksDb db;

    public RocksDbWorldStore(string path) {
        Directory.CreateDirectory(path);
        var options = new DbOptions().SetCreateIfMissing(true);
        db = RocksDbSharp.RocksDb.Open(options, path);
    }

    static byte[] Key(string world, Vector3i c) => Encoding.UTF8.GetBytes($"{world}:{c.X},{c.Y},{c.Z}");

    public void SaveChunk(string world, Vector3i chunk, byte[] data) => db.Put(Key(world, chunk), data);

    public bool TryLoadChunk(string world, Vector3i chunk, out byte[] data) {
        var bytes = db.Get(Key(world, chunk));
        if (bytes is null) {
            data = default!;
            return false;
        }
        data = bytes;
        return true;
    }

    public void Dispose() => db.Dispose();
}
