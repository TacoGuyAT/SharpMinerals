using SharpMinerals.Math;

namespace SharpMinerals.Persistence;

/// <summary>Persists world chunks by (world name, chunk coordinate). The engine loads from here before
/// falling back to generation, and saves chunks modified by gameplay. A host picks the backend.</summary>
public interface IWorldStore {
    void SaveChunk(string world, Vector3i chunk, byte[] data);

    bool TryLoadChunk(string world, Vector3i chunk, out byte[] data);

    /// <summary>Persists a world's loose entities (dropped items, ...) as one blob, replacing the previous one.
    /// Default no-op: a store without entity support simply doesn't persist them.</summary>
    void SaveWorldEntities(string world, byte[] data) { }

    /// <summary>Loads a world's entity blob, or null if none was saved. Default null (no entity support).</summary>
    byte[]? LoadWorldEntities(string world) => null;
}
