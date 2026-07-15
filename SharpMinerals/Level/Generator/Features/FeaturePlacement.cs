using SharpMinerals.Level.Generator.Biomes;

namespace SharpMinerals.Level.Generator.Features;

/// <summary>Where a feature draws its anchor from in a column. <see cref="Terrain"/> scans the density field, so
/// it works at any column (including one rooted in a not-yet-built neighbour cube) but is block-blind - the only
/// border-safe target, hence the only one <see cref="Feature.Scatter"/> accepts. <see cref="Solid"/> scans the
/// finished cube's blocks, so it knows the surface block and sees earlier features, but only within the cube
/// being generated. Reused beyond the world surface: cave floors/ceilings are future block-reading targets that
/// slot in here (mushrooms, glow lichen) without touching features or the fluent chain.</summary>
public enum PlacementTarget { Terrain, Solid }

/// <summary>The fluent WHERE half of a placed feature: pick a scatter pattern (<see cref="Feature.Scatter"/> or
/// <see cref="Feature.PerColumn"/>), narrow it with a <see cref="PlacementTarget"/> (<see cref="On"/>), gate it
/// with a composable <see cref="SurfaceRule"/> (<see cref="Where"/>), and set a per-candidate <see cref="Rarity"/>.
/// <see cref="Bind"/> ties it to a <see cref="FeatureWorld"/>, yielding the <see cref="IChunkDecorator"/> the
/// generator runs. Subclasses own the actual scatter/per-column driving; this base holds the shared knobs.</summary>
public abstract class FeaturePlacement {
    protected readonly Feature Feature;
    protected PlacementTarget Target;
    protected SurfaceRule Rule = SurfaceRule.Always;
    protected Func<IBiome, double>? RarityDensity;
    protected bool RarityFeatureMap;

    protected FeaturePlacement(Feature feature, PlacementTarget defaultTarget) {
        Feature = feature;
        Target = defaultTarget;
    }

    /// <summary>Chooses which anchor source resolves the surface. Each driver has a sensible default (scatter =
    /// <see cref="PlacementTarget.Terrain"/>, per-column = <see cref="PlacementTarget.Solid"/>); override only for
    /// an alternate target the driver supports.</summary>
    public FeaturePlacement On(PlacementTarget target) {
        Target = target;
        return this;
    }

    /// <summary>Requires <paramref name="rule"/> to hold at a candidate column; chains with any earlier rule (AND).</summary>
    public FeaturePlacement Where(SurfaceRule rule) {
        Rule &= rule;
        return this;
    }

    /// <summary>Sets the per-candidate chance the feature roots: a biome-driven base density (e.g. <c>b =>
    /// b.TreeDensity</c>), optionally scaled by the feature-density map (<paramref name="featureMap"/>) for groves
    /// and clearings. Left unset, a candidate that passes <see cref="Where"/> always roots.</summary>
    public FeaturePlacement Rarity(Func<IBiome, double> density, bool featureMap = false) {
        RarityDensity = density;
        RarityFeatureMap = featureMap;
        return this;
    }

    /// <summary>Binds this placement to a world, producing the decorator the generator runs each cube.</summary>
    public abstract IChunkDecorator Bind(FeatureWorld world);
}
