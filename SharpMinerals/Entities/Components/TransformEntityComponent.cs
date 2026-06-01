namespace SharpMinerals.Entities.Components;

// Entities are pure ECS: their data lives in these component structs inside an
// Arch world, not in an object graph. Systems (see World.Tick) operate over them
// in bulk, and the network layer reads them on demand when encoding packets.

/// <summary>
/// World-space placement: position (Minecraft uses doubles for entity coordinates)
/// plus look direction in degrees. The "transform" every visible entity carries.
/// </summary>
public struct TransformEntityComponent {
    public Mfloat X, Y, Z;
    public float Yaw, Pitch;

    public TransformEntityComponent(double x, double y, double z, float yaw = 0f, float pitch = 0f) {
        X = x; Y = y; Z = z;
        Yaw = yaw; Pitch = pitch;
    }

    public readonly Vector3m Position => new(X, Y, Z);
}
