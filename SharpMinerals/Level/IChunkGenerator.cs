using SharpMinerals.Math;

namespace SharpMinerals.Level;

public interface IChunkGenerator {
    public static IChunkGenerator Default => new FlatChunkGenerator();
    Chunk Generate(Vector3i position);
}
