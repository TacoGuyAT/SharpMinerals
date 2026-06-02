using SharpMinerals.Entities;
// Aliased through global:: — the enclosing namespace ends in ".MinRocksDb", which would otherwise shadow it.
using RocksDbKv = global::MinRocksDb.RocksDbKv;

namespace SharpMinerals.Persistence.MinRocksDb;

/// <summary>
/// Native-AOT-safe disk-backed <see cref="IPlayerStore"/> on <see cref="RocksDbKv"/>: one key per player (the
/// UUID's 16 bytes), value is the <see cref="PlayerStateCodec"/> blob. Same on-disk layout as
/// <c>RocksDbPlayerStore</c> (the RocksDbSharp adapter), so saves are portable between the JIT and AOT builds.
/// </summary>
public sealed class MinRocksDbPlayerStore : IPlayerStore, IDisposable {
    readonly RocksDbKv db;

    public MinRocksDbPlayerStore(string path) {
        Directory.CreateDirectory(path);
        db = new RocksDbKv(path);
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
