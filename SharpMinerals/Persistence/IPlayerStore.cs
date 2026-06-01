using SharpMinerals.Entities;

namespace SharpMinerals.Persistence;

/// <summary>
/// Persists players' entity state by UUID across sessions. The engine talks only to this
/// abstraction; the host chooses the backend — the default <see cref="InMemoryPlayerStore"/>,
/// the disk-backed RocksDB store, or any custom implementation. Calls arrive from the server's
/// player join/leave paths, so implementations must be safe under that usage.
/// </summary>
public interface IPlayerStore {
    /// <summary>Stores (or replaces) a player's state.</summary>
    void Save(Guid uuid, PlayerState state);

    /// <summary>Loads a previously-saved state; returns false if this player has none yet.</summary>
    bool TryLoad(Guid uuid, out PlayerState state);
}
