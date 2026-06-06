namespace SharpMinerals.Level.Generator.Biomes;

/// <summary>The set of biomes any mod (vanilla or core) contributes to procedural overworlds. Biomes are
/// registered as <c>seed -&gt; IBiome</c> factories rather than instances, because a biome's seeded detail
/// noise can only be built once the world seed is known; <see cref="Build"/> instantiates them per world.
/// Mirrors the block/item registry pattern: mods register in their initialize phase, the generator reads it
/// at world creation.</summary>
public static class BiomeRegistry {
    static readonly List<Func<int, IBiome>> factories = new();

    /// <summary>Registers a biome factory. Called from a mod's initialize phase.</summary>
    public static void Register(Func<int, IBiome> factory) => factories.Add(factory);

    /// <summary>Instantiates every registered biome for a world with the given seed.</summary>
    public static IReadOnlyList<IBiome> Build(int seed) {
        var biomes = new List<IBiome>(factories.Count);
        foreach (var factory in factories) biomes.Add(factory(seed));
        return biomes;
    }
}
