namespace SharpMinerals.Entities.Components;

/// <summary>Per-tick movement delta integrated into <see cref="TransformEntityComponent"/>.</summary>
[Component]
public struct VelocityEntityComponent {
    Vector3m vector;
    public Mfloat X { readonly get => vector.X; set => vector.X = value; }
    public Mfloat Y { readonly get => vector.Y; set => vector.Y = value; }
    public Mfloat Z { readonly get => vector.Z; set => vector.Z = value; }
    public VelocityEntityComponent(Mfloat x, Mfloat y, Mfloat z) { X = x; Y = y; Z = z; }
}
