using SharpMinerals.Math;

namespace SharpMinerals.Persistence;

/// <summary>Persists ONE world: its chunks by chunk coordinate plus the loose-entity blob. Each world owns
/// its store instance (bound to the world at construction; a disposable store is disposed by
/// <c>World.Unload</c>), so nothing here is keyed by world name. The engine loads from here before falling
/// back to generation, and saves chunks modified by gameplay. A host picks the backend.</summary>
public interface IWorldStore {
    void SaveChunk(Vector3i chunk, byte[] data);

    bool TryLoadChunk(Vector3i chunk, out byte[] data);

    /// <summary>Persists the world's loose entities (dropped items, ...) as one blob, replacing the previous one.
    /// Default no-op: a store without entity support simply doesn't persist them.</summary>
    void SaveEntities(byte[] data) { }

    /// <summary>Loads the world's entity blob, or null if none was saved. Default null (no entity support).</summary>
    byte[]? LoadEntities() => null;
}
