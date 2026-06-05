using RocksDbSharp;

namespace SharpMinerals.Persistence.RocksDb;

/// <summary>
/// Disk-backed <see cref="IEntityStore"/> on RocksDB: one key per entity (the id's 16 bytes), value is the
/// <see cref="EntityCodec"/> blob. Survives restarts. Writes go through RocksDB's write-ahead log, so state is
/// durable per save even on an unclean shutdown.
/// </summary>
public sealed class RocksDbEntityStore : IEntityStore, IDisposable {
    // Fully qualified: the enclosing namespace ends in ".RocksDb", which would otherwise shadow
    // the RocksDbSharp.RocksDb type name.
    readonly RocksDbSharp.RocksDb db;

    /// <summary>Opens (creating if missing) a RocksDB database at <paramref name="path"/>.</summary>
    public RocksDbEntityStore(string path) {
        // RocksDB's CreateIfMissing makes the DB dir itself but not its parents - ensure the
        // full path exists first (e.g. "<data>/world/players").
        Directory.CreateDirectory(path);
        var options = new DbOptions().SetCreateIfMissing(true);
        db = RocksDbSharp.RocksDb.Open(options, path);
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
