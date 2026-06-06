namespace SharpMinerals.Level.Generator.Biomes;

/// <summary>A concrete <see cref="IBiome"/>: a name, a climate-space position, the continuous shape values the
/// source blends across neighbours (<see cref="BaseHeight"/>, <see cref="HeightVariation"/>), an optional
/// per-biome detail field, and the discrete surface rule. Created per world (so a seeded
/// <see cref="Contribution"/> can fold in the world seed) by a factory registered with
/// <see cref="BiomeRegistry"/>.</summary>
public sealed class Biome : IBiome {
    public string Name { get; }
    public ClimatePoint Climate { get; }
    public double BaseHeight { get; }
    public double HeightVariation { get; }
    public IDensity? Contribution { get; }
    public double TreeDensity { get; }
    public double GrassDensity { get; }
    public double FlowerDensity { get; }
    public double DeadBushDensity { get; }
    public ISurfaceRule Surface { get; }

    public Biome(string name, ClimatePoint climate, double baseHeight, double heightVariation,
                 ISurfaceRule surface, IDensity? contribution = null,
                 double treeDensity = 0.0, double grassDensity = 0.0, double flowerDensity = 0.0,
                 double deadBushDensity = 0.0) {
        Name = name;
        Climate = climate;
        BaseHeight = baseHeight;
        HeightVariation = heightVariation;
        Surface = surface;
        Contribution = contribution;
        TreeDensity = treeDensity;
        GrassDensity = grassDensity;
        FlowerDensity = flowerDensity;
        DeadBushDensity = deadBushDensity;
    }

    public override string ToString() => Name;
}
