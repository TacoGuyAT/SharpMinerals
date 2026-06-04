using RocksDbSharp;
using SharpMinerals.Entities;

namespace SharpMinerals.Persistence.RocksDb;

/// <summary>
/// Disk-backed <see cref="IPlayerStore"/> on RocksDB: one key per player (the UUID's 16 bytes),
/// value is the <see cref="PlayerStateCodec"/> blob. Survives restarts. Writes go through
/// RocksDB's write-ahead log, so state is durable per save even on an unclean shutdown.
/// </summary>
public sealed class RocksDbPlayerStore : IPlayerStore, IDisposable {
    // Fully qualified: the enclosing namespace ends in ".RocksDb", which would otherwise shadow
    // the RocksDbSharp.RocksDb type name.
    readonly RocksDbSharp.RocksDb db;

    /// <summary>Opens (creating if missing) a RocksDB database at <paramref name="path"/>.</summary>
    public RocksDbPlayerStore(string path) {
        // RocksDB's CreateIfMissing makes the DB dir itself but not its parents - ensure the
        // full path exists first (e.g. "<data>/world/players").
        Directory.CreateDirectory(path);
        var options = new DbOptions().SetCreateIfMissing(true);
        db = RocksDbSharp.RocksDb.Open(options, path);
    }

    public void Save(Guid uuid, PlayerState state) =>
        db.Put(uuid.ToByteArray(), PlayerStateCodec.Serialize(state));

    public bool TryLoad(Guid uuid, out PlayerState state) {
        var bytes = db.Get(uuid.ToByteArray());
        if (bytes is null) {
            state = default;
            return false;
        }
        state = PlayerStateCodec.Deserialize(bytes);
        return true;
    }

    public void Dispose() => db.Dispose();
}
