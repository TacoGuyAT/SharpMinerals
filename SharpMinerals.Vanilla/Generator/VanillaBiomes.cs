using SharpMinerals.Level.Generator.Biomes;
using SharpMinerals.Network.Nbt;

namespace SharpMinerals.Vanilla.Generator;

/// <summary>The vanilla overworld biomes for P2a, registered as seeded factories with <see cref="BiomeRegistry"/>
/// from <see cref="VanillaMod.OnInitialize"/>. Climate coordinates are roughly [-1, 1] per axis; continentalness
/// separates the ocean (low) from land (high) for the continental look. Shape is authored as baseHeight +
/// heightVariation (blended seam-free) plus, for badlands, an additive ridged mesa contribution. Water fill for
/// the ocean basins comes with the water/wire phase.</summary>
public static class VanillaBiomes {
    public static void Register() {
        // Plains: temperate, fairly dry, inland, flat - grass over dirt (dirt underwater), the occasional tree.
        BiomeRegistry.Register(_ => new Biome("plains",
            new ClimatePoint(Temperature: 0.1, Humidity: 0.0, Continentalness: 0.4, Rockiness: -0.2, Weirdness: 0.0),
            baseHeight: 3.0, heightVariation: 4.0,
            new LayeredSurfaceRule(VanillaMod.GrassBlock, VanillaMod.Dirt, fillerDepth: 3, submergedTop: VanillaMod.Dirt),
            treeDensity: 0.003));

        // Forest: temperate and humid, inland, gently rolling - grass over dirt, densely wooded.
        BiomeRegistry.Register(_ => new Biome("forest",
            new ClimatePoint(Temperature: 0.0, Humidity: 0.6, Continentalness: 0.4, Rockiness: 0.0, Weirdness: 0.0),
            baseHeight: 5.0, heightVariation: 7.0,
            new LayeredSurfaceRule(VanillaMod.GrassBlock, VanillaMod.Dirt, fillerDepth: 3, submergedTop: VanillaMod.Dirt),
            treeDensity: 0.10));

        // Badlands: hot and arid, inland, raised mesa plateaus - red sand over red sand.
        BiomeRegistry.Register(seed => new Biome("badlands",
            new ClimatePoint(Temperature: 0.7, Humidity: -0.7, Continentalness: 0.4, Rockiness: 0.2, Weirdness: 0.0),
            baseHeight: 8.0, heightVariation: 5.0,
            new LayeredSurfaceRule(VanillaMod.RedSand, VanillaMod.RedSand, fillerDepth: 4),
            contribution: new MesaContribution(seed, amplitude: 22.0)));

        // Ocean: deep basin (low continentalness), sandy floor over dirt. Empty until water fill lands.
        BiomeRegistry.Register(_ => new Biome("ocean",
            new ClimatePoint(Temperature: 0.0, Humidity: 0.0, Continentalness: -0.8, Rockiness: -0.5, Weirdness: 0.0),
            baseHeight: -10.0, heightVariation: 3.0,
            new LayeredSurfaceRule(VanillaMod.Sand, VanillaMod.Dirt, fillerDepth: 2)));

        // Wire-side biome registry (climate + render colours), in the SAME order so the ids match the gameplay
        // biomes by name. Plains first so it is the client's empty-chunk default. Colours are vanilla-ish; grass
        // tint is computed from temperature/downfall unless overridden (badlands gets an explicit tan).
        BiomeWireRegistry.Register("plains", temperature: 0.8f, downfall: 0.4f, hasPrecipitation: true, skyColor: 7907327);
        BiomeWireRegistry.Register("forest", temperature: 0.7f, downfall: 0.8f, hasPrecipitation: true, skyColor: 7972607);
        BiomeWireRegistry.Register("badlands", temperature: 2.0f, downfall: 0.0f, hasPrecipitation: false, skyColor: 7254527,
            grassColor: 9470285, foliageColor: 10387789);
        BiomeWireRegistry.Register("ocean", temperature: 0.5f, downfall: 0.5f, hasPrecipitation: true, skyColor: 8103167);
    }
}
