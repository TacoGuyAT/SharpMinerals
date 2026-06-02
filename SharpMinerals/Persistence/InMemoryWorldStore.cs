using System.Collections.Concurrent;
using SharpMinerals.Math;

namespace SharpMinerals.Persistence;

/// <summary><see cref="IWorldStore"/> backed by a dictionary for the process lifetime — exercises the
/// save/load path without a disk backend.</summary>
public sealed class InMemoryWorldStore : IWorldStore {
    readonly ConcurrentDictionary<(string World, Vector3i Chunk), byte[]> chunks = new();

    public void SaveChunk(string world, Vector3i chunk, byte[] data) => chunks[(world, chunk)] = data;

    public bool TryLoadChunk(string world, Vector3i chunk, out byte[] data) =>
        chunks.TryGetValue((world, chunk), out data!);
}
