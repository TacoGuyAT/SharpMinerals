using SharpMinerals.Blocks;

namespace SharpMinerals.Level.Generator.Features;

/// <summary>A composable placement predicate over a <see cref="PlaceContext"/> - "may this feature root here?".
/// Named building blocks (<see cref="AboveSea"/>, <see cref="NotCoastal"/>, <see cref="IsBlock"/>) combine with
/// <c>&amp;</c>, <c>|</c>, and <c>!</c> into the <c>.Where(...)</c> clause of a placement, so the WHERE reads as a
/// sentence and the vocabulary is shared across features. The default value (<see cref="Always"/>) admits every
/// column, so an unset rule is a no-op.</summary>
public readonly struct SurfaceRule {
    readonly Func<PlaceContext, bool>? predicate;

    SurfaceRule(Func<PlaceContext, bool> predicate) => this.predicate = predicate;

    /// <summary>Admits every column - the default rule (an unset <c>.Where</c>).</summary>
    public static SurfaceRule Always => default;

    /// <summary>Wraps an arbitrary test as a rule (the escape hatch when no named block fits).</summary>
    public static SurfaceRule Of(Func<PlaceContext, bool> predicate) => new(predicate);

    /// <summary>Evaluates the rule; a defaulted (Always) rule is always true.</summary>
    public bool Test(in PlaceContext c) => predicate is null || predicate(c);

    public static SurfaceRule operator &(SurfaceRule a, SurfaceRule b) {
        if (a.predicate is null) return b;
        if (b.predicate is null) return a;
        return new(c => a.Test(c) && b.Test(c));
    }

    public static SurfaceRule operator |(SurfaceRule a, SurfaceRule b) {
        if (a.predicate is null || b.predicate is null) return Always; // either side admits all -> admits all
        return new(c => a.Test(c) || b.Test(c));
    }

    public static SurfaceRule operator !(SurfaceRule a) {
        var inner = a; // capture by value so the negation is stable
        return new(c => !inner.Test(c));
    }

    /// <summary>The anchor sits at or above sea level (dry land, not a submerged column).</summary>
    public static readonly SurfaceRule AboveSea = Of(static c => c.Anchor.Y >= WorldDefaults.SeaLevel);

    /// <summary>The column is not surfaced as a coastal beach (so features skip the sand strip at the water's edge).</summary>
    public static readonly SurfaceRule NotCoastal = Of(static c => !c.Source.IsCoastal(c.X, c.Z, c.Anchor.Y));

    /// <summary>The anchor's surface block is <paramref name="block"/> (only meaningful for a block-reading target).</summary>
    public static SurfaceRule IsBlock(BlockType block) => Of(c => c.Anchor.Surface == block);
}
