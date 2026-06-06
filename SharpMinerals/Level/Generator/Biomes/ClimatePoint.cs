namespace SharpMinerals.Level.Generator.Biomes;

/// <summary>A point in the climate space biomes are placed in. Each axis is roughly [-1, 1]. The biome
/// source samples these axes (low-frequency, optionally domain-warped) and weights registered biomes by
/// distance to the sample, giving both the dominant pick and the soft blend weights. Modeled past old-gen's
/// temperature+humidity: continentalness adds land/ocean, rockiness adds erosion-like flat/mountainous, and
/// weirdness drives variants and rivers.</summary>
public readonly record struct ClimatePoint(
    double Temperature,
    double Humidity,
    double Continentalness,
    double Rockiness,
    double Weirdness);
