namespace SharpMinerals.Level.Generator.Biomes;

/// <summary>A biome: where it sits in climate space plus how it shapes and dresses the world. Registered by
/// any mod (vanilla or core), so both contribute biomes. Continuous shape values (<see cref="BaseHeight"/>,
/// <see cref="HeightVariation"/>) are blended across neighbours over the selection kernel - the seam-free
/// part - while the discrete <see cref="Surface"/> rule is hard-picked from the dominant biome. An optional
/// <see cref="Contribution"/> adds the biome's own detail noise scaled by its weight, so unique terrain
/// fades to nothing at the border instead of muddying the blend.
///
/// P1 generates a single implicit overworld and does not consult this; the biome phase wires the source,
/// registry, and the composite density that reads these.</summary>
public interface IBiome {
    /// <summary>This biome's location in the climate space the source weights biomes against.</summary>
    ClimatePoint Climate { get; }

    /// <summary>Base elevation contribution (old-gen baseHeight), blended over the selection kernel.</summary>
    double BaseHeight { get; }

    /// <summary>Vertical amplitude contribution (old-gen heightVariation), blended over the kernel.</summary>
    double HeightVariation { get; }

    /// <summary>Optional per-biome detail field, added scaled by this biome's weight (fades to zero at the
    /// border). Null for a biome that only rides the shared base shape.</summary>
    IDensity? Contribution { get; }

    /// <summary>How cells where this biome dominates are surfaced (top and filler blocks).</summary>
    ISurfaceRule Surface { get; }
}
