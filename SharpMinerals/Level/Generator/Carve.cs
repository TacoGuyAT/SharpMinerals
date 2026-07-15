namespace SharpMinerals.Level.Generator;

/// <summary>A full-resolution edit of an inner density that is constant (or cheaply derived) down a column -
/// rivers, ravines, plateaus, mesas, ... These are sharp, thin features the smooth terrain interpolator (the
/// trilinear lattice) would alias, so they are kept out of it and applied here per cell, on top of the already
/// interpolated terrain. The 2D footprint is computed once per column and reused down the whole vertical stack.
///
/// A subclass supplies only its domain logic: <see cref="BuildColumn"/> (the per-column footprint) and
/// <see cref="Apply"/> (the per-cell edit). The base owns the per-thread column cache, the inner-density dispatch,
/// and an optional coarse per-cube skip (<see cref="CubeMayCarve"/>) for rare carves. <see cref="Apply"/> may make
/// any edit (a blend, a <c>min</c>/<c>max</c> union, an additive cap); <see cref="Lerp"/> is provided for the
/// common "pull the cell toward a target" shape.</summary>
public abstract class Carve<TCol> : IDensity where TCol : struct {
    readonly IDensity inner;
    readonly ColumnCache<TCol> cache;

    protected Carve(IDensity inner) {
        this.inner = inner;
        cache = new ColumnCache<TCol>(BuildColumn, CubeMayCarve);
    }

    public double At(int x, int y, int z) {
        double terrain = inner.At(x, y, z);
        return cache.TryColumn(x, z, out var col) ? Apply(in col, terrain, y) : terrain;
    }

    /// <summary>The per-column footprint, built once per chunk-cube and reused down the vertical stack. Return a
    /// <c>default</c> (or otherwise inert) value where the carve does not reach, so <see cref="Apply"/> is a no-op.</summary>
    protected abstract TCol BuildColumn(int worldX, int worldZ);

    /// <summary>Edits one cell: given its column state, the interpolated <paramref name="terrain"/> density and the
    /// cell's <paramref name="y"/>, return the carved density (return <paramref name="terrain"/> to leave it alone).</summary>
    protected abstract double Apply(in TCol col, double terrain, int y);

    /// <summary>Override to skip whole cubes cheaply: return false when this cube can hold no carve at all (e.g. a
    /// rare carve's low-frequency region gate is absent across the cube, sampled coarsely with a safe margin). When
    /// it returns false the base builds no column and passes the terrain straight through. Default: build every cube.</summary>
    protected virtual bool CubeMayCarve(int cubeX, int cubeZ) => true;

    /// <summary>Pulls <paramref name="terrain"/> toward <paramref name="target"/> by <paramref name="weight"/>
    /// (0 = unchanged, 1 = target) - the common carve shape (a river bed, a ravine opening to air).
    /// <paramref name="weight"/> &lt;= 0 is a no-op.</summary>
    protected static double Lerp(double terrain, double target, double weight)
        => weight <= 0.0 ? terrain : terrain + (target - terrain) * weight;
}
