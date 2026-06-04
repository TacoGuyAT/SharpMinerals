using System.Collections.Concurrent;
using SharpMinerals.Entities;

namespace SharpMinerals.Persistence;

/// <summary>Default <see cref="IPlayerStore"/>: keeps state in a dictionary for the process lifetime
/// (survives reconnects, not restarts). Holds live snapshots directly - no serialization.</summary>
public sealed class InMemoryPlayerStore : IPlayerStore {
    readonly ConcurrentDictionary<Guid, PlayerState> byUuid = new();

    public void Save(Guid uuid, PlayerState state) => byUuid[uuid] = state;

    public bool TryLoad(Guid uuid, out PlayerState state) => byUuid.TryGetValue(uuid, out state);
}
