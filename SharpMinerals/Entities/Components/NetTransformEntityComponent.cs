namespace SharpMinerals.Entities.Components;

// The transform last broadcast to other clients. PlayerMovementSystem diffs the live transform against this
// and relays only when the player actually moved, so a stationary player costs no packets. Seeded to the
// spawn transform so a freshly-joined player (already spawned to others at that position) doesn't re-broadcast.
// Transient: never persisted (it's a per-session broadcast baseline, not world state).
[Component]
public struct NetTransformEntityComponent {
    public Vector3m Position;
    public Mfloat X { get => Position.X; set => Position.X = value; }
    public Mfloat Y { get => Position.Y; set => Position.Y = value; }
    public Mfloat Z { get => Position.Z; set => Position.Z = value; }
    public float Yaw, Pitch;

    public NetTransformEntityComponent(Vector3m position, float yaw = 0f, float pitch = 0f) {
        Position = position;
        Yaw = yaw; Pitch = pitch;
    }
    public NetTransformEntityComponent(Mfloat x, Mfloat y, Mfloat z, float yaw = 0f, float pitch = 0f) {
        Position = new(x, y, z);
        Yaw = yaw;
        Pitch = pitch;
    }
}
