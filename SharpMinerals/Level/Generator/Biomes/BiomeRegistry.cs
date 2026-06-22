namespace SharpMinerals.Level.Generator.Biomes;

/// <summary>The set of biomes any mod (vanilla or core) contributes to procedural overworlds. Biomes are
/// registered as <c>seed -&gt; IBiome</c> factories rather than instances, because a biome's seeded detail
/// noise can only be built once the world seed is known; <see cref="Build"/> instantiates them per world.
/// Mirrors the block/item registry pattern: mods register in their initialize phase, the generator reads it
/// at world creation.</summary>
public static class BiomeRegistry {
    static readonly List<Func<int, IBiome>> factories = new();
    static Func<int, IBiome>? coastalFactory;

    /// <summary>Registers a climate-placed biome factory. Called from a mod's initialize phase.</summary>
    public static void Register(Func<int, IBiome> factory) => factories.Add(factory);

    /// <summary>Registers the single coastal (beach) biome. Unlike a climate biome it is placed by shoreline
    /// ELEVATION, not by a climate-space territory, so it forms thin strips at the water's edge (and along river
    /// banks) instead of claiming a wide continentalness band and flattening that terrain. At most one.</summary>
    public static void RegisterCoastal(Func<int, IBiome> factory) => coastalFactory = factory;

    /// <summary>Instantiates every registered climate biome for a world with the given seed.</summary>
    public static IReadOnlyList<IBiome> Build(int seed) {
        var biomes = new List<IBiome>(factories.Count);
        foreach (var factory in factories) biomes.Add(factory(seed));
        return biomes;
    }

    /// <summary>Instantiates the coastal biome for a world, or null if no mod registered one.</summary>
    public static IBiome? BuildCoastal(int seed) => coastalFactory?.Invoke(seed);
}
