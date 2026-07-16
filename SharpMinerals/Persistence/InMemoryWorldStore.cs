using System.Collections.Concurrent;
using SharpMinerals.Math;

namespace SharpMinerals.Persistence;

/// <summary><see cref="IWorldStore"/> backed by a dictionary for the process lifetime - exercises the
/// save/load path without a disk backend.</summary>
public sealed class InMemoryWorldStore : IWorldStore {
    readonly ConcurrentDictionary<Vector3i, byte[]> chunks = new();
    volatile byte[]? entities;

    public void SaveChunk(Vector3i chunk, byte[] data) => chunks[chunk] = data;

    public bool TryLoadChunk(Vector3i chunk, out byte[] data) => chunks.TryGetValue(chunk, out data!);

    public void SaveEntities(byte[] data) => entities = data;

    public byte[]? LoadEntities() => entities;
}
