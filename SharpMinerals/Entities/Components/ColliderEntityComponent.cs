namespace SharpMinerals.Entities.Components;

/// <summary>An entity's axis-aligned collision box: centred on its transform in X/Z, rising <see cref="Height"/>
/// from its feet. Read by both the physics step (terrain collision) and <see cref="CollisionFeedbackEntityComponent"/>
/// (entity overlap).</summary>
public struct ColliderEntityComponent {
    public double Width;
    public double Height;

    public ColliderEntityComponent(double width, double height) {
        Width = width;
        Height = height;
    }

    public readonly double HalfWidth => Width / 2;
}
