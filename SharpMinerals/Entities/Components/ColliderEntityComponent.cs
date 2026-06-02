namespace SharpMinerals.Entities.Components;

/// <summary>An entity's axis-aligned collision box: centred on its transform in X/Z, rising <see cref="Height"/>
/// from its feet. Read by both the physics step (terrain collision) and <see cref="CollisionFeedbackEntityComponent"/>
/// (entity overlap).</summary>
[Component]
public struct ColliderEntityComponent {
    public Mfloat Width;
    public Mfloat Height;

    public ColliderEntityComponent(Mfloat width, Mfloat height) {
        Width = width;
        Height = height;
    }

    public readonly Mfloat HalfWidth => Width / 2;
}
