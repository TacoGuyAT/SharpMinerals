// Aliased through global:: - the enclosing namespace ends in ".MinRocksDb", which would otherwise shadow it.
using RocksDbKv = global::MinRocksDb.RocksDbKv;

namespace SharpMinerals.Persistence.MinRocksDb;

/// <summary>
/// Native-AOT-safe disk-backed <see cref="IEntityStore"/> on <see cref="RocksDbKv"/>: one key per entity (the id's
/// 16 bytes), value is the <see cref="EntityCodec"/> blob. Same on-disk layout as <c>RocksDbEntityStore</c> (the
/// RocksDbSharp adapter), so saves are portable between the JIT and AOT builds.
/// </summary>
public sealed class MinRocksDbEntityStore : IEntityStore, IDisposable {
    readonly RocksDbKv db;

    public MinRocksDbEntityStore(string path) {
        Directory.CreateDirectory(path);
        db = new RocksDbKv(path);
    }

    public void Save(Guid id, byte[] data) => db.Put(id.ToByteArray(), data);

    public bool TryLoad(Guid id, out byte[] data) {
        var bytes = db.Get(id.ToByteArray());
        if (bytes is null) {
            data = default!;
            return false;
        }
        data = bytes;
        return true;
    }

    public void Dispose() => db.Dispose();
}
