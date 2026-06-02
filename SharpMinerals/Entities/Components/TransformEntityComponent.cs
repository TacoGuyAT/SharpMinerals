namespace SharpMinerals.Entities.Components;

/// <summary>World-space placement: position (doubles) plus look direction in degrees. The transform every
/// visible entity carries.</summary>
public struct TransformEntityComponent {
    public Mfloat X, Y, Z;
    public float Yaw, Pitch;

    public TransformEntityComponent(double x, double y, double z, float yaw = 0f, float pitch = 0f) {
        X = x; Y = y; Z = z;
        Yaw = yaw; Pitch = pitch;
    }

    public readonly Vector3m Position => new(X, Y, Z);
}
