using SharpMinerals.Math;

namespace SharpMinerals.Level;

/// <summary>
/// The simplest possible generator: every chunk is empty air. Useful as a default
/// and for tests that don't care about terrain.
/// </summary>
public sealed class VoidChunkGenerator : IChunkGenerator {
    public Chunk Generate(Vector3i position) => new(position);
}
