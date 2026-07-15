namespace SharpMinerals.Level.Generator.Features;

/// <summary>A decoration stamped after terrain and surface - a tree, a flora patch, an ore vein - registered by
/// any mod (vanilla or core). A feature is the WHAT: it declares its <see cref="Extent"/> (footprint, for the
/// driver's scatter reach and cube culling) and stamps its cells in world coordinates through a clipping
/// <see cref="CubeSink"/>, so a feature crossing a cube border is written correctly by each cube it reaches into
/// with no shared state. The WHERE - how thickly it scatters and what columns qualify - is a separate
/// <see cref="FeaturePlacement"/> built by <see cref="Scatter"/> or <see cref="PerColumn"/>. Placement is
/// deterministic and stateless: all randomness comes from the seeded <see cref="PlaceContext.Rng"/>.</summary>
public abstract class Feature {
    /// <summary>This feature's footprint around its anchor; the placement driver uses it to size scatter reach
    /// and to cull cubes the feature cannot reach.</summary>
    public abstract Extent Extent { get; }

    /// <summary>Stamps the feature for one resolved placement. Write in absolute world coordinates through
    /// <paramref name="sink"/> (it clips to the current cube); draw any randomness from <c>ctx.Rng</c>.</summary>
    public abstract void Place(in PlaceContext ctx, CubeSink sink);

    /// <summary>Scatters this feature on a jittered grid - one candidate per <paramref name="spacing"/>-cell,
    /// nudged up to <paramref name="jitter"/> blocks - so features cross cube borders seamlessly. The surface is
    /// found from the density field (<see cref="PlacementTarget.Terrain"/>), the only border-safe target.</summary>
    public FeaturePlacement Scatter(int spacing, int jitter) => new ScatterPlacement(this, spacing, jitter);

    /// <summary>Places this feature at most once per column of the cube being generated, reading the finished
    /// cube's blocks to find the surface (<see cref="PlacementTarget.Solid"/>). For single-cell features that
    /// never cross a border (ground cover) and want to see earlier features (not sit under a canopy).</summary>
    public FeaturePlacement PerColumn() => new PerColumnPlacement(this);
}
