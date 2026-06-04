using System.Text;
using SharpMinerals.Math;
// Aliased through global:: - the enclosing namespace ends in ".MinRocksDb", which would otherwise shadow it.
using RocksDbKv = global::MinRocksDb.RocksDbKv;

namespace SharpMinerals.Persistence.MinRocksDb;

/// <summary>
/// Native-AOT-safe disk-backed <see cref="IWorldStore"/> on <see cref="RocksDbKv"/>: one key per chunk
/// (<c>"&lt;world&gt;:x,y,z"</c> bytes), value is the <see cref="ChunkCodec"/> blob. Same on-disk layout as
/// <c>RocksDbWorldStore</c> (the RocksDbSharp adapter), so a world is portable between the JIT and AOT builds.
/// </summary>
public sealed class MinRocksDbWorldStore : IWorldStore, IDisposable {
    readonly RocksDbKv db;

    public MinRocksDbWorldStore(string path) {
        Directory.CreateDirectory(path);
        db = new RocksDbKv(path);
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
