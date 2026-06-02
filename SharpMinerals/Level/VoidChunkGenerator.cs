using SharpMinerals.Math;

namespace SharpMinerals.Level;

/// <summary>The simplest generator: every chunk is empty air.</summary>
public sealed class VoidChunkGenerator : IChunkGenerator {
    public Chunk Generate(Vector3i position) => new(position);
}
