namespace SharpMinerals.Entities.Components;

/// <summary>
/// An entity's axis-aligned collision box: centred on its <see cref="TransformEntityComponent"/> in X/Z and
/// rising <see cref="Height"/> from its feet. Shared by two systems — the physics step (sweeping
/// the box against solid blocks for terrain collision) and <see cref="CollisionFeedbackEntityComponent"/> (testing
/// box overlap against other entities). One box definition, read by both.
/// </summary>
public struct ColliderEntityComponent {
    public double Width;
    public double Height;

    public ColliderEntityComponent(double width, double height) {
        Width = width;
        Height = height;
    }

    public readonly double HalfWidth => Width / 2;
}
