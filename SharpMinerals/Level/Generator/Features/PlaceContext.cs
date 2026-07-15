using SharpMinerals.Level.Generator.Biomes;

namespace SharpMinerals.Level.Generator.Features;

/// <summary>Everything a predicate, rarity roll, or feature needs about one candidate placement: the world column
/// (<see cref="X"/>, <see cref="Z"/>), the resolved <see cref="Anchor"/> it would root at, the <see cref="Biome"/>
/// there, the shared <see cref="Source"/> (for feature-density, coastline, ... queries), and a <see cref="Rng"/>
/// already seeded to the feature's own cell so all its rolls are independent and reproducible. A plain value with
/// no behaviour - the placement driver fills it in and hands it to <see cref="SurfaceRule"/> and
/// <see cref="Feature.Place"/>.</summary>
public readonly struct PlaceContext {
    public readonly int X, Z;
    public readonly Anchor Anchor;
    public readonly IBiome Biome;
    public readonly BiomeSource Source;
    public readonly WorldRng Rng;

    public PlaceContext(int x, int z, Anchor anchor, IBiome biome, BiomeSource source, WorldRng rng) {
        X = x;
        Z = z;
        Anchor = anchor;
        Biome = biome;
        Source = source;
        Rng = rng;
    }
}
