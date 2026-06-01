using SharpMinerals.Math;

namespace SharpMinerals.Level;

/// <summary>Produces a populated <see cref="Chunk"/> for a given chunk coordinate.</summary>
public interface IChunkGenerator {
    public static IChunkGenerator Default => new FlatChunkGenerator();
    Chunk Generate(Vector3i position);
}
