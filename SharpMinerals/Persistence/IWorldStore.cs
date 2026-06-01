using SharpMinerals.Math;

namespace SharpMinerals.Persistence;

/// <summary>
/// Persists world chunks by (world name, chunk coordinate). The engine loads a chunk from here
/// before falling back to generation, and saves chunks modified by gameplay. A host picks the
/// backend — RocksDB for disk, the in-memory store for tests, or none (generate every time).
/// </summary>
public interface IWorldStore {
    /// <summary>Stores (or replaces) a chunk's serialized data.</summary>
    void SaveChunk(string world, Vector3i chunk, byte[] data);

    /// <summary>Loads a previously-saved chunk; returns false if none is stored.</summary>
    bool TryLoadChunk(string world, Vector3i chunk, out byte[] data);
}
