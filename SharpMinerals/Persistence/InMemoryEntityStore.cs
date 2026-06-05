using System.Collections.Concurrent;

namespace SharpMinerals.Persistence;

/// <summary>Default <see cref="IEntityStore"/>: keeps the encoded blobs in a dictionary for the process lifetime
/// (survives reconnects, not restarts).</summary>
public sealed class InMemoryEntityStore : IEntityStore {
    readonly ConcurrentDictionary<Guid, byte[]> byId = new();

    public void Save(Guid id, byte[] data) => byId[id] = data;

    public bool TryLoad(Guid id, out byte[] data) => byId.TryGetValue(id, out data!);
}
