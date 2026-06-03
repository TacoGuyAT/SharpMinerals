using SharpMinerals.Math;

namespace SharpMinerals.Level;

public interface IChunkGenerator {
    /// <summary>The core default: an empty (void) world. The flat-world generator now lives in the
    /// <c>SharpMinerals.Minecraft</c> mod (it needs vanilla blocks); hosts inject it explicitly.</summary>
    public static IChunkGenerator Default => new VoidChunkGenerator();
    Chunk Generate(Vector3i position);
}
