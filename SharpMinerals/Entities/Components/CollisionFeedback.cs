using ArchEntity = Arch.Core.Entity;

namespace SharpMinerals.Entities.Components;

/// <summary>
/// Gives an entity an axis-aligned collision box — centred on its <see cref="Transform"/>
/// in X/Z and rising <see cref="Height"/> from its feet — and records which other
/// colliding entities overlap it each tick. <see cref="Touching"/> is the "feedback"
/// other systems read (e.g. item pickup). The collision pass in <c>World.Tick</c>
/// clears and refills it.
/// </summary>
public struct CollisionFeedback {
    public double Width;
    public double Height;

    /// <summary>Entities overlapping this one as of the last collision pass.</summary>
    public readonly List<ArchEntity> Touching;

    public CollisionFeedback(double width, double height) {
        Width = width;
        Height = height;
        Touching = new List<ArchEntity>();
    }
}
