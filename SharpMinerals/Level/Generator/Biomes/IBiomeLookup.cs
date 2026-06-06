namespace SharpMinerals.Level.Generator.Biomes;

/// <summary>An optional capability a chunk generator can expose: the biome at a block column. Used now for a
/// debug overlay and, later, for the wire biome encoding. Generators without biomes (flat, void) simply do not
/// implement it.</summary>
public interface IBiomeLookup {
    string? BiomeNameAt(int x, int z);
}
