using System.Collections.Concurrent;
using SharpMinerals.Entities;

namespace SharpMinerals.Persistence;

/// <summary>
/// Default <see cref="IPlayerStore"/>: keeps state in a dictionary for the process lifetime
/// (survives reconnects, not restarts). Holds the live snapshot objects directly — no
/// serialization — so it has zero dependencies and is what tests and the zero-config server use.
/// </summary>
public sealed class InMemoryPlayerStore : IPlayerStore {
    readonly ConcurrentDictionary<Guid, PlayerState> byUuid = new();

    public void Save(Guid uuid, PlayerState state) => byUuid[uuid] = state;

    public bool TryLoad(Guid uuid, out PlayerState state) => byUuid.TryGetValue(uuid, out state);
}
