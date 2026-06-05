namespace SharpMinerals.Persistence;

/// <summary>Persists entity state by id across sessions. The blob is whatever <see cref="EntityCodec"/> produces
/// (a generic length-prefixed component bag), so the store is entity-kind-agnostic - players today (keyed by their
/// UUID), other persistent entities later (keyed by their own id). The host chooses the backend (default
/// <see cref="InMemoryEntityStore"/>, RocksDB, or custom). Called from join/leave paths, so must be safe under that.</summary>
public interface IEntityStore {
    void Save(Guid id, byte[] data);

    bool TryLoad(Guid id, out byte[] data);
}
