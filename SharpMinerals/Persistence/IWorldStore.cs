using SharpMinerals.Math;

namespace SharpMinerals.Persistence;

/// <summary>Persists world chunks by (world name, chunk coordinate). The engine loads from here before
/// falling back to generation, and saves chunks modified by gameplay. A host picks the backend.</summary>
public interface IWorldStore {
    void SaveChunk(string world, Vector3i chunk, byte[] data);

    bool TryLoadChunk(string world, Vector3i chunk, out byte[] data);
}
