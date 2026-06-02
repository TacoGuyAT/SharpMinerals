using SharpMinerals.Entities;

namespace SharpMinerals.Persistence;

/// <summary>Persists players' entity state by UUID across sessions. The host chooses the backend (default
/// <see cref="InMemoryPlayerStore"/>, RocksDB, or custom). Called from join/leave paths, so must be safe under that.</summary>
public interface IPlayerStore {
    void Save(Guid uuid, PlayerState state);

    bool TryLoad(Guid uuid, out PlayerState state);
}
